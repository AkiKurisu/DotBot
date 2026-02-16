using System.ClientModel;
using System.Collections.Concurrent;
using DotBot.Abstractions;
using DotBot.CLI;
using DotBot.Configuration;
using DotBot.Context;
using DotBot.DashBoard;
using DotBot.Memory;
using DotBot.QQ;
using DotBot.Security;
using DotBot.Skills;
using DotBot.Tools;
using DotBot.Tools.Providers.Channels;
using DotBot.Tools.Providers.Core;
using DotBot.Tools.Providers.System;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace DotBot.Agents;

/// <summary>
/// Factory for creating AI agents with tool aggregation from providers.
/// Tools are aggregated from registered <see cref="IAgentToolProvider"/> instances.
/// </summary>
public sealed class AgentFactory
{
    private readonly AppConfig _config;
    private readonly MemoryStore _memoryStore;
    private readonly SkillsLoader _skillsLoader;
    private readonly string _cortexBotPath;
    private readonly string _workspacePath;
    private readonly ChatClient _chatClient;
    private readonly ContextCompactor? _contextCompactor;
    private readonly ConcurrentDictionary<string, TokenTracker> _tokenTrackers = new();
    private readonly TraceCollector? _traceCollector;
    private readonly HashSet<string> _globalEnabledToolNames;
    private readonly ToolProviderContext _toolProviderContext;
    private readonly IReadOnlyList<IAgentToolProvider> _toolProviders;

    // Legacy fields for backward compatibility (WeComTools accessor)
    private readonly WeComTools? _weComTools;

    /// <summary>
    /// Creates a new AgentFactory with tool providers.
    /// This is the preferred constructor for the new provider-based architecture.
    /// </summary>
    public AgentFactory(
        string cortexBotPath,
        string workspacePath,
        AppConfig config,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        IApprovalService approvalService,
        PathBlacklist? blacklist,
        IEnumerable<IAgentToolProvider>? toolProviders,
        ToolProviderContext? toolProviderContext = null,
        TraceCollector? traceCollector = null)
    {
        _config = config;
        _memoryStore = memoryStore;
        _skillsLoader = skillsLoader;
        _cortexBotPath = cortexBotPath;
        _workspacePath = workspacePath;
        _traceCollector = traceCollector;
        _globalEnabledToolNames = ResolveGlobalEnabledToolNames(_config);

        _chatClient = new OpenAIClient(new ApiKeyCredential(config.ApiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(config.EndPoint)
        }).GetChatClient(_config.Model);

        if (config.MaxContextTokens > 0)
            _contextCompactor = new ContextCompactor(_chatClient);

        // Initialize WeCom tools for backward compatibility (WeComTools property accessor)
        if ((config.WeCom.Enabled && !string.IsNullOrWhiteSpace(config.WeCom.WebhookUrl)) || config.WeComBot.Enabled)
            _weComTools = new WeComTools(config.WeCom.WebhookUrl);

        // Build tool provider context
        _toolProviderContext = toolProviderContext ?? new ToolProviderContext
        {
            Config = config,
            ChatClient = _chatClient,
            WorkspacePath = workspacePath,
            BotPath = cortexBotPath,
            MemoryStore = memoryStore,
            SkillsLoader = skillsLoader,
            ApprovalService = approvalService,
            PathBlacklist = blacklist,
            TraceCollector = traceCollector
        };

        // Use provided providers or build default set
        _toolProviders = toolProviders != null
            ? toolProviders.ToList()
            : BuildDefaultToolProviders();
    }

    /// <summary>
    /// Gets the WeCom tools instance for direct access (used by Heartbeat service).
    /// </summary>
    public WeComTools? WeComTools => _weComTools;

    /// <summary>
    /// Gets the last created tools for inspection.
    /// </summary>
    public IReadOnlyList<AITool>? LastCreatedTools { get; private set; }

    /// <summary>
    /// Gets the context compactor for large conversations.
    /// </summary>
    public ContextCompactor? Compactor => _contextCompactor;

    /// <summary>
    /// Gets the maximum context tokens from configuration.
    /// </summary>
    public int MaxContextTokens => _config.MaxContextTokens;

    /// <summary>
    /// Gets or creates a token tracker for the specified session.
    /// </summary>
    public TokenTracker GetOrCreateTokenTracker(string sessionKey)
    {
        return _tokenTrackers.GetOrAdd(sessionKey, _ => new TokenTracker());
    }

    /// <summary>
    /// Removes the token tracker for the specified session.
    /// </summary>
    public void RemoveTokenTracker(string sessionKey)
    {
        _tokenTrackers.TryRemove(sessionKey, out _);
    }

    /// <summary>
    /// Creates default tools by aggregating all registered tool providers.
    /// Tools are ordered by provider priority (lower priority value = earlier in list).
    /// </summary>
    public List<AITool> CreateDefaultTools()
    {
        var tools = _toolProviders
            .OrderBy(p => p.Priority)
            .SelectMany(p => p.CreateTools(_toolProviderContext))
            .ToList();

        // Apply global tool filtering if configured
        if (_globalEnabledToolNames.Count > 0)
        {
            tools = tools
                .Where(t => _globalEnabledToolNames.Contains(t.Name))
                .ToList();
        }

        // Initialize tool icon registry with tool instances for icon mapping
        InitializeToolIconRegistry();

        return tools;
    }

    /// <summary>
    /// Creates tools filtered by the specified enabled tool names.
    /// </summary>
    public List<AITool> CreateFilteredTools(IReadOnlyList<string> enabledToolNames)
    {
        var allTools = CreateDefaultTools();
        if (enabledToolNames.Count == 0)
            return allTools;

        return allTools
            .Where(t => enabledToolNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Creates the default AI agent with all registered tools.
    /// </summary>
    public AIAgent CreateDefaultAgent()
    {
        return CreateAgentWithTools(CreateDefaultTools());
    }

    /// <summary>
    /// Creates an AI agent with the specified tools.
    /// </summary>
    public AIAgent CreateAgentWithTools(List<AITool> tools)
    {
        LastCreatedTools = tools;

        var chatClientBuilder = new ChatClientBuilder(_chatClient.AsIChatClient());
        chatClientBuilder.Use(innerClient => new FunctionInvokingChatClient(innerClient)
        {
            MaximumIterationsPerRequest = _config.MaxToolCallRounds
        });
        var configuredChatClient = chatClientBuilder.Build();

        var options = new ChatClientAgentOptions
        {
            Name = "DotBot",
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = _config.SystemInstructions,
                Tools = tools
            },
            AIContextProviderFactory = (_, _) => new ValueTask<AIContextProvider>(
                new MemoryContextProvider(
                    _memoryStore,
                    _skillsLoader,
                    _cortexBotPath,
                    _workspacePath,
                    _config.SystemInstructions,
                    _traceCollector,
                    () => tools.Select(t => t.Name).ToArray()))
        };

        return configuredChatClient.AsAIAgent(options);
    }

    /// <summary>
    /// Creates a tracing chat client for debugging and monitoring.
    /// </summary>
    public IChatClient CreateTracingChatClient(TraceCollector? traceCollector = null)
    {
        var chatClientBuilder = new ChatClientBuilder(_chatClient.AsIChatClient());
        chatClientBuilder.Use(innerClient => new ToolCallFilteringChatClient(innerClient));
        if (traceCollector != null)
        {
            chatClientBuilder.Use(innerClient => new TracingChatClient(innerClient, traceCollector));
        }
        chatClientBuilder.Use(innerClient => new FunctionInvokingChatClient(innerClient)
        {
            MaximumIterationsPerRequest = _config.MaxToolCallRounds
        });
        return chatClientBuilder.Build();
    }

    private static HashSet<string> ResolveGlobalEnabledToolNames(AppConfig config)
    {
        return config.EnabledTools.Count == 0
            ? []
            : new HashSet<string>(config.EnabledTools, StringComparer.OrdinalIgnoreCase);
    }

    private static List<IAgentToolProvider> BuildDefaultToolProviders()
    {
        // All providers self-check availability in CreateTools()
        // No need for conditional logic here - providers return empty list when not applicable
        return
        [
            new CoreToolProvider(),
            new WeComToolProvider(),
            new QQToolProvider(),
            new CronToolProvider(),
            new McpToolProvider()
        ];
    }

    private void InitializeToolIconRegistry()
    {
        // Build tool instances for icon registry (for backward compatibility)
        var userDotBotPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bot"));

        var subAgentManager = new SubAgentManager(
            _chatClient,
            _workspacePath,
            _config.SubagentMaxToolCallRounds,
            maxConcurrency: _config.SubagentMaxConcurrency,
            shellTimeout: _config.Tools.Shell.Timeout,
            blacklist: _toolProviderContext.PathBlacklist);

        var toolInstances = new List<object>
        {
            new AgentTools(subAgentManager),
            new FileTools(
                _workspacePath,
                _config.Tools.File.RequireApprovalOutsideWorkspace,
                _config.Tools.File.MaxFileSize,
                _toolProviderContext.ApprovalService,
                _toolProviderContext.PathBlacklist,
                trustedReadPaths: [userDotBotPath]),
            new ShellTools(
                _workspacePath,
                _config.Tools.Shell.Timeout,
                _config.Tools.Shell.RequireApprovalOutsideWorkspace,
                _config.Tools.Shell.MaxOutputLength,
                approvalService: _toolProviderContext.ApprovalService,
                blacklist: _toolProviderContext.PathBlacklist),
            new WebTools(
                _config.Tools.Web.MaxChars,
                _config.Tools.Web.Timeout,
                _config.Tools.Web.SearchMaxResults,
                _config.Tools.Web.SearchProvider)
        };

        if (_weComTools != null)
            toolInstances.Add(_weComTools);

        if (_toolProviderContext.ChannelClient is QQBotClient qqClient)
            toolInstances.Add(new QQTools(qqClient));

        ToolIconRegistry.Initialize(toolInstances.ToArray());
    }
}

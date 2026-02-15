using System.ClientModel;
using System.Collections.Concurrent;
using DotBot.CLI;
using DotBot.Context;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.QQ;
using DotBot.Security;
using DotBot.Skills;
using DotBot.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace DotBot.Agents;

public sealed class AgentFactory
{
    private readonly AppConfig _config;
    
    private readonly MemoryStore _memoryStore;
    
    private readonly SkillsLoader _skillsLoader;
    
    private readonly string _cortexBotPath;
    
    private readonly string _workspacePath;
    
    private readonly ChatClient _chatClient;
    
    private readonly AgentTools _agentTools;

    private readonly FileTools _fileTools;

    private readonly ShellTools _shellTools;

    private readonly WebTools _webTools;

    private readonly WeComTools? _weComTools;

    private readonly QQTools? _qqTools;

    private readonly CronTools? _cronTools;

    private readonly ContextCompactor? _contextCompactor;

    private readonly McpClientManager? _mcpClientManager;

    private readonly ConcurrentDictionary<string, TokenTracker> _tokenTrackers = new();

    private readonly TraceCollector? _traceCollector;

    private readonly HashSet<string> _globalEnabledToolNames;

    public AgentFactory(
        string cortexBotPath,
        string workspacePath,
        AppConfig config,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        IApprovalService approvalService,
        PathBlacklist? blacklist = null,
        QQBotClient? qqBotClient = null,
        CronTools? cronTools = null,
        McpClientManager? mcpClientManager = null,
        TraceCollector? traceCollector = null)
    {
        _config = config;
        _memoryStore = memoryStore;
        _skillsLoader = skillsLoader;
        _cortexBotPath = cortexBotPath;
        _workspacePath = workspacePath;
        _cronTools = cronTools;
        _mcpClientManager = mcpClientManager;
        _traceCollector = traceCollector;
        _globalEnabledToolNames = ResolveGlobalEnabledToolNames(_config);

        _chatClient = new OpenAIClient(new ApiKeyCredential(config.ApiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(config.EndPoint)
        }).GetChatClient(_config.Model);

        var subAgentManager = new SubAgentManager(
            _chatClient, 
            _workspacePath,
            _config.SubagentMaxToolCallRounds,
            maxConcurrency: _config.SubagentMaxConcurrency,
            shellTimeout: _config.Tools.Shell.Timeout,
            blacklist: blacklist);
        _agentTools = new AgentTools(subAgentManager);
        var userDotBotPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bot"));
        _fileTools = new FileTools(
            _workspacePath,
            _config.Tools.File.RequireApprovalOutsideWorkspace, 
            _config.Tools.File.MaxFileSize,
            approvalService,
            blacklist,
            trustedReadPaths: [userDotBotPath]);
        _shellTools = new ShellTools(
            _workspacePath,
            _config.Tools.Shell.Timeout, 
            _config.Tools.Shell.RequireApprovalOutsideWorkspace,
            _config.Tools.Shell.MaxOutputLength,
            approvalService: approvalService,
            blacklist: blacklist);
        _webTools = new WebTools(
            _config.Tools.Web.MaxChars,
            _config.Tools.Web.Timeout,
            _config.Tools.Web.SearchMaxResults,
            _config.Tools.Web.SearchProvider);

        if (config.MaxContextTokens > 0)
            _contextCompactor = new ContextCompactor(_chatClient);

        // Initialize WeCom tools if group webhook is configured or WeCom Bot is enabled
        if ((config.WeCom.Enabled && !string.IsNullOrWhiteSpace(config.WeCom.WebhookUrl)) || config.WeComBot.Enabled)
            _weComTools = new WeComTools(config.WeCom.WebhookUrl);

        // Initialize QQ tools if client is available
        if (qqBotClient != null)
            _qqTools = new QQTools(qqBotClient);

        // Initialize tool icon registry with all tool instances
        var toolInstances = new List<object> { _agentTools, _fileTools, _shellTools, _webTools };
        if (_weComTools != null) toolInstances.Add(_weComTools);
        if (_qqTools != null) toolInstances.Add(_qqTools);
        ToolIconRegistry.Initialize(toolInstances.ToArray());
    }

    public WeComTools? WeComTools => _weComTools;

    public IReadOnlyList<AITool>? LastCreatedTools { get; private set; }

    public ContextCompactor? Compactor => _contextCompactor;

    public int MaxContextTokens => _config.MaxContextTokens;

    public TokenTracker GetOrCreateTokenTracker(string sessionKey)
    {
        return _tokenTrackers.GetOrAdd(sessionKey, _ => new TokenTracker());
    }

    public void RemoveTokenTracker(string sessionKey)
    {
        _tokenTrackers.TryRemove(sessionKey, out _);
    }

    public List<AITool> CreateDefaultTools()
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(_agentTools.SpawnSubagent),
            AIFunctionFactory.Create(_fileTools.ReadFile),
            AIFunctionFactory.Create(_fileTools.WriteFile),
            AIFunctionFactory.Create(_fileTools.EditFile),
            AIFunctionFactory.Create(_fileTools.GrepFiles),
            AIFunctionFactory.Create(_fileTools.FindFiles),
            AIFunctionFactory.Create(_shellTools.Exec),
            AIFunctionFactory.Create(_webTools.WebSearch),
            AIFunctionFactory.Create(_webTools.WebFetch),
        };

        if (_cronTools != null)
            tools.Add(AIFunctionFactory.Create(_cronTools.Cron));

        if (_weComTools != null)
        {
            tools.Add(AIFunctionFactory.Create(_weComTools.WeComNotify));
            tools.Add(AIFunctionFactory.Create(_weComTools.WeComSendVoice));
            tools.Add(AIFunctionFactory.Create(_weComTools.WeComSendFile));
        }

        if (_qqTools != null)
        {
            tools.Add(AIFunctionFactory.Create(_qqTools.QQSendGroupVoice));
            tools.Add(AIFunctionFactory.Create(_qqTools.QQSendPrivateVoice));
            tools.Add(AIFunctionFactory.Create(_qqTools.QQSendGroupVideo));
            tools.Add(AIFunctionFactory.Create(_qqTools.QQSendPrivateVideo));
            tools.Add(AIFunctionFactory.Create(_qqTools.QQUploadGroupFile));
            tools.Add(AIFunctionFactory.Create(_qqTools.QQUploadPrivateFile));
        }

        if (_mcpClientManager != null)
        {
            foreach (var mcpTool in _mcpClientManager.Tools)
                tools.Add(mcpTool);
        }

        if (_globalEnabledToolNames.Count == 0)
            return tools;

        return tools
            .Where(t => _globalEnabledToolNames.Contains(t.Name))
            .ToList();
    }

    public List<AITool> CreateFilteredTools(IReadOnlyList<string> enabledToolNames)
    {
        var allTools = CreateDefaultTools();
        if (enabledToolNames.Count == 0)
            return allTools;

        var filtered = allTools.Where(t =>
            enabledToolNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        return filtered;
    }

    private static HashSet<string> ResolveGlobalEnabledToolNames(AppConfig config)
    {
        return config.EnabledTools.Count == 0
            ? []
            : new HashSet<string>(config.EnabledTools, StringComparer.OrdinalIgnoreCase);
    }

    public AIAgent CreateDefaultAgent()
    {
        return CreateAgentWithTools(CreateDefaultTools());
    }

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
}

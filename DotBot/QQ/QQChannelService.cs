using System.ClientModel;
using DotBot.Abstractions;
using DotBot.Agents;
using DotBot.Configuration;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Heartbeat;
using DotBot.Hosting;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.Modules;
using DotBot.Security;
using DotBot.Skills;
using DotBot.Tools;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotBot.QQ;

/// <summary>
/// Gateway channel service for QQ Bot. Manages the QQ WebSocket connection,
/// channel adapter, and agent lifecycle as part of a multi-channel gateway.
/// </summary>
public sealed class QQChannelService : IChannelService
{
    private readonly IServiceProvider _sp;
    private readonly AppConfig _config;
    private readonly DotBotPaths _paths;
    private readonly SessionStore _sessionStore;
    private readonly MemoryStore _memoryStore;
    private readonly SkillsLoader _skillsLoader;
    private readonly PathBlacklist _blacklist;
    private readonly McpClientManager _mcpClientManager;
    private readonly QQBotClient _qqClient;
    private readonly QQPermissionService _permissionService;
    private readonly QQApprovalService _qqApprovalService;
    private readonly ModuleRegistry _moduleRegistry;

    private QQChannelAdapter? _adapter;

    public string Name => "qq";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService? ApprovalService => _qqApprovalService;

    /// <inheritdoc />
    public object? ChannelClient => _qqClient;

    public QQChannelService(
        IServiceProvider sp,
        AppConfig config,
        DotBotPaths paths,
        SessionStore sessionStore,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        PathBlacklist blacklist,
        McpClientManager mcpClientManager,
        QQBotClient qqClient,
        QQPermissionService permissionService,
        QQApprovalService qqApprovalService,
        ModuleRegistry moduleRegistry)
    {
        _sp = sp;
        _config = config;
        _paths = paths;
        _sessionStore = sessionStore;
        _memoryStore = memoryStore;
        _skillsLoader = skillsLoader;
        _blacklist = blacklist;
        _mcpClientManager = mcpClientManager;
        _qqClient = qqClient;
        _permissionService = permissionService;
        _qqApprovalService = qqApprovalService;
        _moduleRegistry = moduleRegistry;
    }

    private AgentFactory BuildAgentFactory()
    {
        var cronTools = _sp.GetService<CronTools>();
        var traceCollector = _sp.GetService<TraceCollector>();

        // Collect tool providers from modules
        var toolProviders = ToolProviderCollector.Collect(_moduleRegistry, _config);

        return new AgentFactory(
            _paths.BotPath, _paths.WorkspacePath, _config,
            _memoryStore, _skillsLoader, _qqApprovalService, _blacklist,
            toolProviders: toolProviders,
            toolProviderContext: new ToolProviderContext
            {
                Config = _config,
                ChatClient = new OpenAIClient(
                    new ApiKeyCredential(_config.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(_config.EndPoint) })
                    .GetChatClient(_config.Model),
                WorkspacePath = _paths.WorkspacePath,
                BotPath = _paths.BotPath,
                MemoryStore = _memoryStore,
                SkillsLoader = _skillsLoader,
                ApprovalService = _qqApprovalService,
                PathBlacklist = _blacklist,
                CronTools = cronTools,
                McpClientManager = _mcpClientManager.Tools.Count > 0 ? _mcpClientManager : null,
                TraceCollector = traceCollector,
                ChannelClient = _qqClient
            },
            traceCollector: traceCollector);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var agentFactory = BuildAgentFactory();
        var agent = agentFactory.CreateDefaultAgent();
        var traceCollector = _sp.GetService<TraceCollector>();
        var tokenUsageStore = _sp.GetService<TokenUsageStore>();

        _adapter = new QQChannelAdapter(
            _qqClient, agent, _sessionStore,
            _permissionService, _qqApprovalService,
            heartbeatService: HeartbeatService,
            cronService: CronService,
            agentFactory: agentFactory,
            traceCollector: traceCollector,
            tokenUsageStore: tokenUsageStore);

        await _qqClient.StartAsync(cancellationToken);

        AnsiConsole.MarkupLine(
            $"[green][[Gateway]][/] QQ Bot listening on ws://{_config.QQBot.Host}:{_config.QQBot.Port}/");

        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
    }

    public async Task StopAsync()
    {
        await _qqClient.StopAsync();
    }

    public async Task DeliverMessageAsync(string target, string content)
    {
        // target is either "group:<groupId>" for group messages or a plain user id for private
        if (target.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            var groupIdStr = target["group:".Length..];
            if (long.TryParse(groupIdStr, out var groupId))
            {
                await _qqClient.SendGroupMessageAsync(groupId, content);
                return;
            }
        }

        if (long.TryParse(target, out var userId))
            await _qqClient.SendPrivateMessageAsync(userId, content);
    }

    public async ValueTask DisposeAsync()
    {
        if (_adapter != null)
            await _adapter.DisposeAsync();
    }
}

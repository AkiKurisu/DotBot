using System.ClientModel;
using DotBot.Abstractions;
using DotBot.Agents;
using DotBot.Configuration;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Hosting;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.Modules;
using DotBot.Security;
using DotBot.Skills;
using DotBot.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotBot.WeCom;

/// <summary>
/// Gateway channel service for WeCom Bot. Manages the ASP.NET Core HTTP server,
/// channel adapter, and agent lifecycle as part of a multi-channel gateway.
/// </summary>
public sealed class WeComChannelService : IChannelService
{
    private readonly IServiceProvider _sp;
    private readonly AppConfig _config;
    private readonly DotBotPaths _paths;
    private readonly SessionStore _sessionStore;
    private readonly MemoryStore _memoryStore;
    private readonly SkillsLoader _skillsLoader;
    private readonly PathBlacklist _blacklist;
    private readonly McpClientManager _mcpClientManager;
    private readonly WeComBotRegistry _registry;
    private readonly WeComPermissionService _permissionService;
    private readonly WeComApprovalService _wecomApprovalService;
    private readonly ModuleRegistry _moduleRegistry;

    private WebApplication? _webApp;
    private WeComChannelAdapter? _adapter;

    public string Name => "wecom";

    public WeComChannelService(
        IServiceProvider sp,
        AppConfig config,
        DotBotPaths paths,
        SessionStore sessionStore,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        PathBlacklist blacklist,
        McpClientManager mcpClientManager,
        WeComBotRegistry registry,
        WeComPermissionService permissionService,
        WeComApprovalService wecomApprovalService,
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
        _registry = registry;
        _permissionService = permissionService;
        _wecomApprovalService = wecomApprovalService;
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
            _memoryStore, _skillsLoader, _wecomApprovalService, _blacklist,
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
                ApprovalService = _wecomApprovalService,
                PathBlacklist = _blacklist,
                CronTools = cronTools,
                McpClientManager = _mcpClientManager.Tools.Count > 0 ? _mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var agentFactory = BuildAgentFactory();
        var agent = agentFactory.CreateDefaultAgent();
        var traceCollector = _sp.GetService<TraceCollector>();
        var tokenUsageStore = _sp.GetService<TokenUsageStore>();

        _adapter = new WeComChannelAdapter(
            agent, _sessionStore, _registry,
            _permissionService, _wecomApprovalService,
            heartbeatService: null,
            cronService: null,
            agentFactory: agentFactory,
            traceCollector: traceCollector,
            tokenUsageStore: tokenUsageStore);

        var builder = WebApplication.CreateBuilder();
        _webApp = builder.Build();

        var logger = new WeComServerLogger();
        var server = new WeComBotServer(_registry, logger: logger);
        server.MapRoutes(_webApp);

        var url = $"https://{_config.WeComBot.Host}:{_config.WeComBot.Port}";
        AnsiConsole.MarkupLine($"[green][[Gateway]][/] WeCom Bot listening on {Markup.Escape(url)}");
        foreach (var path in _registry.GetAllPaths())
        {
            AnsiConsole.MarkupLine($"[grey]  - {Markup.Escape(url + path)}[/]");
        }

        _ = _webApp.RunAsync(url);

        // Wait for cancellation
        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
    }

    public async Task StopAsync()
    {
        if (_webApp != null)
            await _webApp.StopAsync();
    }

    public Task DeliverMessageAsync(string target, string content)
    {
        // WeCom delivery uses the outgoing webhook URL (no per-target routing in bot webhook mode)
        if (!string.IsNullOrWhiteSpace(_config.WeCom.WebhookUrl))
        {
            var wecomTools = new WeComTools(_config.WeCom.WebhookUrl);
            return wecomTools.SendTextAsync(content);
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_adapter != null)
            await _adapter.DisposeAsync();
        if (_webApp != null)
            await _webApp.DisposeAsync();
    }
}

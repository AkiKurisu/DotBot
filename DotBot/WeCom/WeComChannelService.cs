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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotBot.WeCom;

/// <summary>
/// Gateway channel service for WeCom Bot. Manages the ASP.NET Core HTTP server,
/// channel adapter, and agent lifecycle as part of a multi-channel gateway.
/// </summary>
public sealed class WeComChannelService(
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
    : IChannelService
{
    private WebApplication? _webApp;
    private WeComChannelAdapter? _adapter;

    public string Name => "wecom";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService ApprovalService => wecomApprovalService;

    /// <inheritdoc />
    public object? ChannelClient => null;

    private AgentFactory BuildAgentFactory()
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();

        // Collect tool providers from modules
        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        return new AgentFactory(
            paths.BotPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, wecomApprovalService, blacklist,
            toolProviders: toolProviders,
            toolProviderContext: new ToolProviderContext
            {
                Config = config,
                ChatClient = new OpenAIClient(
                    new ApiKeyCredential(config.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(config.EndPoint) })
                    .GetChatClient(config.Model),
                WorkspacePath = paths.WorkspacePath,
                BotPath = paths.BotPath,
                MemoryStore = memoryStore,
                SkillsLoader = skillsLoader,
                ApprovalService = wecomApprovalService,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var agentFactory = BuildAgentFactory();
        var agent = agentFactory.CreateDefaultAgent();
        var traceCollector = sp.GetService<TraceCollector>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        _adapter = new WeComChannelAdapter(
            agent, sessionStore, registry,
            permissionService, wecomApprovalService,
            heartbeatService: HeartbeatService,
            cronService: CronService,
            agentFactory: agentFactory,
            traceCollector: traceCollector,
            tokenUsageStore: tokenUsageStore);

        var builder = WebApplication.CreateBuilder();
        _webApp = builder.Build();

        var logger = new WeComServerLogger();
        var server = new WeComBotServer(registry, logger: logger);
        server.MapRoutes(_webApp);

        var url = $"https://{config.WeComBot.Host}:{config.WeComBot.Port}";
        AnsiConsole.MarkupLine($"[green][[Gateway]][/] WeCom Bot listening on {Markup.Escape(url)}");
        foreach (var path in registry.GetAllPaths())
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
        if (!string.IsNullOrWhiteSpace(config.WeCom.WebhookUrl))
        {
            var wecomTools = new WeComTools(config.WeCom.WebhookUrl);
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

using DotBot.Agents;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Heartbeat;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.Security;
using DotBot.Skills;
using DotBot.WeCom;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DotBot.Hosting;

public sealed class WeComBotHost(
    IServiceProvider sp,
    AppConfig config,
    DotBotPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    CronService cronService,
    McpClientManager mcpClientManager) : IDotBotHost
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var traceStore = sp.GetService<TraceStore>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var registry = new WeComBotRegistry();

        foreach (var robotConfig in config.WeComBot.Robots)
        {
            if (string.IsNullOrEmpty(robotConfig.Token) || string.IsNullOrEmpty(robotConfig.AesKey))
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Skipping WeCom bot {Markup.Escape(robotConfig.Path)}: Token or AesKey is empty[/]");
                continue;
            }

            registry.Register(
                path: robotConfig.Path,
                token: robotConfig.Token,
                encodingAesKey: robotConfig.AesKey);
        }

        if (config.WeComBot.DefaultRobot != null &&
            !string.IsNullOrEmpty(config.WeComBot.DefaultRobot.Token) &&
            !string.IsNullOrEmpty(config.WeComBot.DefaultRobot.AesKey))
        {
            AnsiConsole.MarkupLine("[grey][[WeCom]][/] [green]Default robot configured[/]");
        }

        var wecomPermissionService = new WeComPermissionService(
            config.WeComBot.AdminUsers,
            config.WeComBot.WhitelistedUsers,
            config.WeComBot.WhitelistedChats);

        var wecomApprovalService = new WeComApprovalService(
            wecomPermissionService, config.WeComBot.ApprovalTimeoutSeconds);

        var agentFactory = new AgentFactory(
            paths.BotPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, wecomApprovalService, blacklist,
            cronTools: cronTools,
            mcpClientManager: mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
            traceCollector: traceCollector);

        var agent = agentFactory.CreateDefaultAgent();
        var runner = new AgentRunner(agent, sessionStore, agentFactory, traceCollector);

        DashBoardServer? dashBoardServer = null;
        if (config.DashBoard.Enabled && traceStore != null)
        {
            dashBoardServer = new DashBoardServer();
            dashBoardServer.Start(traceStore, config, tokenUsageStore);
        }

        using var heartbeatService = new HeartbeatService(
            paths.BotPath,
            onHeartbeat: runner.RunAsync,
            intervalSeconds: config.Heartbeat.IntervalSeconds,
            enabled: config.Heartbeat.Enabled);

        if (config.Heartbeat.NotifyAdmin && agentFactory.WeComTools != null)
        {
            heartbeatService.OnResult = async result =>
            {
                try { await agentFactory.WeComTools.SendTextAsync($"[Heartbeat] {result}"); }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(
                        $"[grey][[Heartbeat]][/] [red]WeCom notify failed: {Markup.Escape(ex.Message)}[/]");
                }
            };
        }

        cronService.OnJob = async job =>
        {
            var sessionKey = $"cron:{job.Id}";
            var result = await runner.RunAsync(job.Payload.Message, sessionKey);
            if (job.Payload.Deliver && result != null)
            {
                try
                {
                    if (job.Payload.Channel == "wecom" && agentFactory.WeComTools != null)
                        await agentFactory.WeComTools.SendTextAsync(result);
                    else if (agentFactory.WeComTools != null)
                        await agentFactory.WeComTools.SendTextAsync($"[Cron: {job.Name}] {result}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Cron] Delivery failed: {ex.Message}");
                }
            }
        };

        await using var wecomAdapter = new WeComChannelAdapter(
            agent, sessionStore, registry,
            wecomPermissionService, wecomApprovalService,
            heartbeatService: heartbeatService,
            cronService: cronService,
            traceCollector: traceCollector,
            tokenUsageStore: tokenUsageStore);

        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        var logger = new WeComServerLogger();
        var server = new WeComBotServer(registry, logger: logger);
        server.MapRoutes(app);

        if (config.Heartbeat.Enabled)
        {
            heartbeatService.Start();
            AnsiConsole.MarkupLine($"[green]Heartbeat started (interval: {config.Heartbeat.IntervalSeconds}s)[/]");
        }
        if (config.Cron.Enabled)
        {
            cronService.Start();
            AnsiConsole.MarkupLine($"[green]Cron service started ({cronService.ListJobs().Count} jobs)[/]");
        }

        var url = $"https://{config.WeComBot.Host}:{config.WeComBot.Port}";
        AnsiConsole.MarkupLine($"[green]WeCom Bot listening on {Markup.Escape(url)}[/]");
        AnsiConsole.MarkupLine("[grey]Registered bots:[/]");
        foreach (var path in registry.GetAllPaths())
        {
            AnsiConsole.MarkupLine($"[grey]  - {Markup.Escape(url + path)}[/]");
        }
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop...[/]");

        _ = app.RunAsync(url);
        await WaitForShutdownSignalAsync(cancellationToken);

        AnsiConsole.MarkupLine("[yellow]Shutting down WeCom Bot...[/]");
        heartbeatService.Stop();
        cronService.Stop();
        await app.StopAsync();

        if (dashBoardServer != null)
            await dashBoardServer.DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private static async Task WaitForShutdownSignalAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult();
        };
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;
    }
}

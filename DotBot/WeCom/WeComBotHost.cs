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
using DotBot.Security;
using DotBot.Skills;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotBot.WeCom;

public sealed class WeComBotHost(
    IServiceProvider sp,
    AppConfig config,
    DotBotPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    CronService cronService,
    McpClientManager mcpClientManager,
    WeComBotRegistry registry,
    WeComPermissionService wecomPermissionService,
    WeComApprovalService wecomApprovalService) : IDotBotHost
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var traceStore = sp.GetService<TraceStore>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var agentFactory = new AgentFactory(
            paths.BotPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, wecomApprovalService, blacklist,
            toolProviders: null,
            toolProviderContext: new ToolProviderContext
            {
                Config = config,
                ChatClient = new OpenAIClient(new ApiKeyCredential(config.ApiKey), new OpenAIClientOptions
                {
                    Endpoint = new Uri(config.EndPoint)
                }).GetChatClient(config.Model),
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
            agentFactory: agentFactory,
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

using System.ClientModel;
using DotBot.Abstractions;
using DotBot.Agents;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Heartbeat;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.QQ;
using DotBot.Security;
using DotBot.Skills;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotBot.Hosting;

public sealed class QQBotHost(
    IServiceProvider sp,
    AppConfig config,
    DotBotPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    CronService cronService,
    McpClientManager mcpClientManager,
    QQBotClient qqClient,
    QQPermissionService permissionService,
    QQApprovalService qqApprovalService) : IDotBotHost
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var traceStore = sp.GetService<TraceStore>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var agentFactory = new AgentFactory(
            paths.BotPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, qqApprovalService, blacklist,
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
                ApprovalService = qqApprovalService,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector,
                ChannelClient = qqClient
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

        if (config.Heartbeat.NotifyAdmin)
        {
            heartbeatService.OnResult = async result =>
            {
                foreach (var adminId in config.QQBot.AdminUsers)
                {
                    try { await qqClient.SendPrivateMessageAsync(adminId, $"[Heartbeat] {result}"); }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine(
                            $"[grey][[Heartbeat]][/] [red]Failed to notify admin {adminId}: {Markup.Escape(ex.Message)}[/]");
                    }
                }
                if (agentFactory.WeComTools != null)
                {
                    try { await agentFactory.WeComTools.SendTextAsync($"[Heartbeat] {result}"); }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine(
                            $"[grey][[Heartbeat]][/] [red]WeCom notify failed: {Markup.Escape(ex.Message)}[/]");
                    }
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
                    else if (job.Payload.Channel != null && long.TryParse(job.Payload.Channel, out var groupId))
                        await qqClient.SendGroupMessageAsync(groupId, result);
                    else if (job.Payload.To != null && long.TryParse(job.Payload.To, out var userId))
                        await qqClient.SendPrivateMessageAsync(userId, result);
                    else if (job.Payload.CreatorId != null)
                    {
                        if (job.Payload.CreatorSource == "wecom" && agentFactory.WeComTools != null)
                            await agentFactory.WeComTools.SendTextAsync($"[Cron: {job.Name}] {result}");
                        else if (long.TryParse(job.Payload.CreatorId, out var creatorId))
                            await qqClient.SendPrivateMessageAsync(creatorId, $"[Cron: {job.Name}] {result}");
                    }
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"[Cron] Delivery failed: {ex.Message}");
                }
            }
        };

        await using var qqAdapter = new QQChannelAdapter(
            qqClient, agent, sessionStore,
            permissionService, qqApprovalService,
            heartbeatService: heartbeatService,
            cronService: cronService,
            agentFactory: agentFactory,
            traceCollector: traceCollector,
            tokenUsageStore: tokenUsageStore);

        await qqClient.StartAsync();

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

        AnsiConsole.MarkupLine($"[green]QQ Bot listening on ws://{config.QQBot.Host}:{config.QQBot.Port}/[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop...[/]");

        await WaitForShutdownSignalAsync(cancellationToken);

        AnsiConsole.MarkupLine("[yellow]Shutting down QQ Bot...[/]");
        heartbeatService.Stop();
        cronService.Stop();
        await qqClient.StopAsync();

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

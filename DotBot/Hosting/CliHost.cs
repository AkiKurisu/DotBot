using System.ClientModel;
using DotBot.Abstractions;
using DotBot.Agents;
using DotBot.CLI;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Heartbeat;
using DotBot.Localization;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.Security;
using DotBot.Skills;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace DotBot.Hosting;

public sealed class CliHost(
    IServiceProvider sp,
    AppConfig config,
    DotBotPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    CronService cronService,
    McpClientManager mcpClientManager,
    LanguageService languageService,
    ConsoleApprovalService cliApprovalService) : IDotBotHost
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var traceStore = sp.GetService<TraceStore>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var agentFactory = new AgentFactory(
            paths.BotPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, cliApprovalService, blacklist,
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
                ApprovalService = cliApprovalService,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector);

        var agent = agentFactory.CreateDefaultAgent();
        var runner = new AgentRunner(agent, sessionStore, agentFactory, traceCollector);

        DashBoardServer? dashBoardServer = null;
        string? dashBoardUrl = null;
        if (config.DashBoard.Enabled && traceStore != null)
        {
            dashBoardServer = new DashBoardServer();
            dashBoardServer.Start(traceStore, config, tokenUsageStore);
            dashBoardUrl = $"http://{config.DashBoard.Host}:{config.DashBoard.Port}/dashboard";
        }

        using var heartbeatService = new HeartbeatService(
            paths.BotPath,
            onHeartbeat: runner.RunAsync,
            intervalSeconds: config.Heartbeat.IntervalSeconds,
            enabled: false);

        var repl = new ReplHost(agent, sessionStore, skillsLoader,
            paths.WorkspacePath, paths.BotPath, config,
            heartbeatService: heartbeatService, cronService: cronService,
            agentFactory: agentFactory, mcpClientManager: mcpClientManager,
            traceCollector: traceCollector, dashBoardUrl: dashBoardUrl,
            languageService: languageService);

        cronService.OnJob = async job =>
        {
            var sessionKey = $"cron:{job.Id}";
            await runner.RunAsync(job.Payload.Message, sessionKey);
            repl.ReprintPrompt();
        };

        if (config.Cron.Enabled)
            cronService.Start();

        await repl.RunAsync(cancellationToken);

        cronService.Stop();

        if (dashBoardServer != null)
            await dashBoardServer.DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

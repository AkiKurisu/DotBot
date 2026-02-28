using System.ClientModel;
using System.Reflection;
using DotBot.Agents;
using DotBot.Commands.Custom;
using DotBot.Configuration;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Gateway;
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

namespace DotBot.Acp;

/// <summary>
/// Host for ACP (Agent Client Protocol) mode.
/// Communicates with the editor/IDE over stdio using JSON-RPC.
/// </summary>
public sealed class AcpHost(
    IServiceProvider sp,
    AppConfig config,
    DotBotPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    ModuleRegistry moduleRegistry) : IDotBotHost
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // stdout is reserved for JSON-RPC; redirect all console diagnostics to stderr
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });

        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var customCommandLoader = sp.GetService<CustomCommandLoader>();

        ToolProviderCollector.ScanToolIcons(Assembly.GetExecutingAssembly());
        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        using var acpLogger = AcpLogger.Create(paths.BotPath, config.DebugMode);

        await using var transport = AcpTransport.CreateStdio();
        transport.Logger = acpLogger;
        transport.StartReaderLoop();
        var approvalService = new AcpApprovalService(transport);

        var planStore = new PlanStore(paths.BotPath);

        var agentFactory = new AgentFactory(
            paths.BotPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, approvalService, blacklist,
            toolProviders: toolProviders,
            toolProviderContext: new Abstractions.ToolProviderContext
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
                ApprovalService = approvalService,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector,
            customCommandLoader: customCommandLoader,
            planStore: planStore);

        var agent = agentFactory.CreateDefaultAgent();
        var handler = new AcpHandler(
            transport, sessionStore, agentFactory, agent,
            approvalService, paths.WorkspacePath,
            customCommandLoader, traceCollector, acpLogger,
            planStore: planStore);

        AnsiConsole.MarkupLine("[green][[ACP]][/] DotBot ACP agent started (stdio)");
        await handler.RunAsync(cancellationToken);
        AnsiConsole.MarkupLine("[grey][[ACP]][/] ACP agent stopped");
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

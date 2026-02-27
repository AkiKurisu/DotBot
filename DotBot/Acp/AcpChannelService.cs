using System.ClientModel;
using System.Reflection;
using DotBot.Abstractions;
using DotBot.Agents;
using DotBot.Commands.Custom;
using DotBot.Configuration;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Gateway;
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

namespace DotBot.Acp;

/// <summary>
/// Gateway channel service for ACP.
/// Manages the stdio ACP transport as part of a multi-channel gateway.
/// </summary>
public sealed class AcpChannelService(
    IServiceProvider sp,
    AppConfig config,
    DotBotPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    ModuleRegistry moduleRegistry)
    : IChannelService
{
    public string Name => "acp";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService ApprovalService { get; } = new AutoApproveApprovalService();

    /// <inheritdoc />
    public object? ChannelClient => null;

    public async Task StartAsync(CancellationToken cancellationToken)
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
        var acpApproval = new AcpApprovalService(transport);

        var agentFactory = new AgentFactory(
            paths.BotPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, acpApproval, blacklist,
            toolProviders: toolProviders,
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
                ApprovalService = acpApproval,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector,
            customCommandLoader: customCommandLoader);

        var agent = agentFactory.CreateDefaultAgent();
        var sessionGate = sp.GetRequiredService<SessionGate>();
        var runner = new AgentRunner(agent, sessionStore, agentFactory, traceCollector, sessionGate);

        var handler = new AcpHandler(
            transport, sessionStore, agentFactory, agent,
            acpApproval, paths.WorkspacePath,
            customCommandLoader, traceCollector, acpLogger);

        AnsiConsole.MarkupLine("[green][[Gateway]][/] ACP channel started (stdio)");

        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());

        // Run handler in background, complete when either handler exits or cancellation
        var handlerTask = handler.RunAsync(cancellationToken);
        await Task.WhenAny(handlerTask, tcs.Task);
    }

    public Task StopAsync()
    {
        return Task.CompletedTask;
    }

    public Task DeliverMessageAsync(string target, string content)
    {
        // ACP channel has no proactive message delivery capability
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        // Nothing to dispose
    }
}

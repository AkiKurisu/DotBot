using DotBot.Abstractions;
using DotBot.CLI.Factories;
using DotBot.Configuration;
using DotBot.Hosting;
using DotBot.Modules;
using DotBot.Security;
using DotBot.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.CLI;

/// <summary>
/// CLI module for interactive console-based interaction.
/// This is the default module when no other modules are enabled.
/// Priority: 0 (lowest - used as fallback)
/// </summary>
[DotBotModule("cli", Priority = 0, Description = "CLI module for interactive console-based interaction (default fallback)")]
public sealed partial class CliModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config)
    {
        // CLI is the fallback mode when no channel is active
        return !config.QQBot.Enabled && !config.WeComBot.Enabled && !config.Api.Enabled;
    }

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // Register ConsoleApprovalService (depends on ApprovalStore from AddDotBot)
        services.AddSingleton(sp =>
        {
            var approvalStore = sp.GetRequiredService<ApprovalStore>();
            var factory = new ConsoleApprovalServiceFactory();
            return (ConsoleApprovalService)factory.Create(new ApprovalServiceContext
            {
                Config = context.Config,
                WorkspacePath = context.Paths.WorkspacePath,
                ApprovalStore = approvalStore
            });
        });
    }

    /// <inheritdoc />
    public override IEnumerable<IAgentToolProvider> GetToolProviders()
        => [new CoreToolProvider()];
}

/// <summary>
/// Host factory for CLI mode.
/// </summary>
[HostFactory("cli")]
public sealed class CliHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotBotHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<CliHost>(serviceProvider);
    }
}

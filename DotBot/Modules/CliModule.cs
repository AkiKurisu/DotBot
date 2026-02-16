using DotBot.Abstractions;
using DotBot.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Modules;

/// <summary>
/// CLI module for interactive console-based interaction.
/// This is the default module when no other modules are enabled.
/// Priority: 0 (lowest - used as fallback)
/// </summary>
[DotBotModule("cli", Priority = 0, Description = "CLI module for interactive console-based interaction (default fallback)")]
public sealed class CliModule : ModuleBase
{
    /// <inheritdoc />
    public override string Name => "cli";

    /// <inheritdoc />
    public override int Priority => 0;

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config)
    {
        // CLI is enabled when no other modules are enabled
        // Support both old AppConfig access and new module config
        return !config.QQBot.Enabled && !config.WeComBot.Enabled && !config.Api.Enabled;
    }

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // CLI-specific services are created in CliHost via factories
        // Core services (ApprovalStore, etc.) are already registered in AddDotBot
    }
}

/// <summary>
/// Host factory for CLI mode.
/// </summary>
[HostFactory("cli")]
public sealed class CliHostFactory : IHostFactory
{
    /// <inheritdoc />
    public string ModeName => "cli";

    /// <inheritdoc />
    public IDotBotHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<CliHost>(serviceProvider);
    }
}

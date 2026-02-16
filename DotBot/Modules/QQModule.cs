using DotBot.Abstractions;
using DotBot.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Modules;

/// <summary>
/// QQ Bot module for QQ platform interaction via OneBot protocol.
/// Priority: 30 (highest)
/// </summary>
[DotBotModule("qq", Priority = 30, Description = "QQ Bot module for QQ platform interaction via OneBot protocol")]
public sealed class QQModule : ModuleBase
{
    /// <inheritdoc />
    public override string Name => "qq";

    /// <inheritdoc />
    public override int Priority => 30;

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config)
    {
        // Support both old AppConfig access and new module config
        return config.QQBot.Enabled;
    }

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // QQ-specific services are created in QQBotHost via factories
        // Module config is already registered in AddModuleConfigurations
    }
}

/// <summary>
/// Host factory for QQ mode.
/// </summary>
[HostFactory("qq")]
public sealed class QQHostFactory : IHostFactory
{
    /// <inheritdoc />
    public string ModeName => "qq";

    /// <inheritdoc />
    public IDotBotHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<QQBotHost>(serviceProvider);
    }
}

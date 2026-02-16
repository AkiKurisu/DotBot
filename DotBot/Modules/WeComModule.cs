using DotBot.Abstractions;
using DotBot.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Modules;

/// <summary>
/// WeCom (Enterprise WeChat) module for enterprise messaging platform interaction.
/// Priority: 20
/// </summary>
[DotBotModule("wecom", Priority = 20, Description = "WeCom (Enterprise WeChat) module for enterprise messaging platform interaction")]
public sealed class WeComModule : ModuleBase
{
    /// <inheritdoc />
    public override string Name => "wecom";

    /// <inheritdoc />
    public override int Priority => 20;

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config)
    {
        // Support both old AppConfig access and new module config
        return config.WeComBot.Enabled;
    }

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // WeCom-specific services are created in WeComBotHost via factories
        // Module config is already registered in AddModuleConfigurations
    }
}

/// <summary>
/// Host factory for WeCom mode.
/// </summary>
[HostFactory("wecom")]
public sealed class WeComHostFactory : IHostFactory
{
    /// <inheritdoc />
    public string ModeName => "wecom";

    /// <inheritdoc />
    public IDotBotHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<WeComBotHost>(serviceProvider);
    }
}

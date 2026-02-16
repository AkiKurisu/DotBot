using DotBot.Abstractions;
using DotBot.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Modules;

/// <summary>
/// API module for OpenAI-compatible HTTP API interaction.
/// Priority: 10
/// </summary>
[DotBotModule("api", Priority = 10, Description = "API module for OpenAI-compatible HTTP API interaction")]
public sealed class ApiModule : ModuleBase
{
    /// <inheritdoc />
    public override string Name => "api";

    /// <inheritdoc />
    public override int Priority => 10;

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config)
    {
        // Support both old AppConfig access and new module config
        return config.Api.Enabled;
    }

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // API mode doesn't require additional services beyond core
        // Module config is already registered in AddModuleConfigurations
    }
}

/// <summary>
/// Host factory for API mode.
/// </summary>
[HostFactory("api")]
public sealed class ApiHostFactory : IHostFactory
{
    /// <inheritdoc />
    public string ModeName => "api";

    /// <inheritdoc />
    public IDotBotHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<ApiHost>(serviceProvider);
    }
}

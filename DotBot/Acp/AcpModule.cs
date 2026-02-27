using DotBot.Abstractions;
using DotBot.Configuration;
using DotBot.Hosting;
using DotBot.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Acp;

/// <summary>
/// ACP module for Agent Client Protocol interaction with code editors/IDEs.
/// </summary>
[DotBotModule("acp", Priority = 200, Description = "ACP module for Agent Client Protocol (stdio) interaction with code editors")]
public sealed partial class AcpModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.Acp.Enabled;
}

/// <summary>
/// Host factory for ACP mode.
/// </summary>
[HostFactory("acp")]
public sealed class AcpHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotBotHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<AcpHost>(serviceProvider);
    }
}

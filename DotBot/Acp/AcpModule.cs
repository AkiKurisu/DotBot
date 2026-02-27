using DotBot.Abstractions;
using DotBot.Configuration;
using DotBot.Hosting;
using DotBot.Modules;
using DotBot.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Acp;

/// <summary>
/// ACP module for Agent Client Protocol interaction with code editors/IDEs.
/// Priority: 15 (higher than API to prefer ACP when both are enabled)
/// </summary>
[DotBotModule("acp", Priority = 15, Description = "ACP module for Agent Client Protocol (stdio) interaction with code editors")]
public sealed partial class AcpModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.Acp.Enabled;

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // ACP mode uses its own AcpApprovalService created at runtime in AcpHost,
        // since it needs the transport instance. No pre-registered approval service needed.
    }

    /// <inheritdoc />
    public override IEnumerable<IAgentToolProvider> GetToolProviders()
        => [];

    /// <inheritdoc />
    public override IChannelService CreateChannelService(IServiceProvider sp, ModuleContext context)
        => ActivatorUtilities.CreateInstance<AcpChannelService>(sp);
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

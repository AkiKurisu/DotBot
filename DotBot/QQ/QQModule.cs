using DotBot.Abstractions;
using DotBot.Configuration;
using DotBot.Modules;
using DotBot.QQ.Factories;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.QQ;

/// <summary>
/// QQ Bot module for QQ platform interaction via OneBot protocol.
/// Priority: 30 (highest)
/// </summary>
[DotBotModule("qq", Priority = 30, Description = "QQ Bot module for QQ platform interaction via OneBot protocol")]
public sealed partial class QQModule : ModuleBase
{
    private readonly QQConfigValidator _validator = new();

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.QQBot.Enabled;

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
        => _validator.Validate(config.QQBot);

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        var config = context.Config.QQBot;

        // Register QQBotClient
        services.AddSingleton(_ => QQClientFactory.CreateClient(context));

        // Register QQPermissionService
        services.AddSingleton(QQClientFactory.CreatePermissionService(context));

        // Register QQApprovalService (depends on QQBotClient and QQPermissionService)
        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<QQBotClient>();
            var permission = sp.GetRequiredService<QQPermissionService>();
            var factory = new QQApprovalServiceFactory();
            return (QQApprovalService)factory.Create(new ApprovalServiceContext
            {
                Config = context.Config,
                WorkspacePath = context.Paths.WorkspacePath,
                ChannelClient = client,
                PermissionService = permission,
                ApprovalTimeoutSeconds = config.ApprovalTimeoutSeconds
            });
        });
    }

    /// <inheritdoc />
    public override IEnumerable<IAgentToolProvider> GetToolProviders()
        => [new QQToolProvider()];

    /// <inheritdoc />
    public override IChannelService? CreateChannelService(IServiceProvider sp, ModuleContext context)
        => ActivatorUtilities.CreateInstance<QQChannelService>(sp);
}

using DotBot.Abstractions;
using DotBot.Configuration;
using DotBot.Hosting;
using DotBot.WeCom;
using DotBot.WeCom.Factories;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Modules;

/// <summary>
/// WeCom (Enterprise WeChat) module for enterprise messaging platform interaction.
/// Priority: 20
/// </summary>
[DotBotModule("wecom", Priority = 20, Description = "WeCom (Enterprise WeChat) module for enterprise messaging platform interaction")]
public sealed partial class WeComModule : ModuleBase
{
    private readonly WeComConfigValidator _validator = new();

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.WeComBot.Enabled;

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
        => _validator.Validate(config.WeComBot);

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        var config = context.Config.WeComBot;

        // Register WeComBotRegistry
        services.AddSingleton(WeComClientFactory.CreateRegistry(context));

        // Register WeComPermissionService
        services.AddSingleton(WeComClientFactory.CreatePermissionService(context));

        // Register WeComApprovalService (depends on WeComPermissionService)
        services.AddSingleton(sp =>
        {
            var permission = sp.GetRequiredService<WeComPermissionService>();
            var factory = new WeComApprovalServiceFactory();
            return (WeComApprovalService)factory.Create(new ApprovalServiceContext
            {
                Config = context.Config,
                WorkspacePath = context.Paths.WorkspacePath,
                PermissionService = permission,
                ApprovalTimeoutSeconds = config.ApprovalTimeoutSeconds
            });
        });
    }
}

/// <summary>
/// Host factory for WeCom mode.
/// </summary>
[HostFactory("wecom")]
public sealed class WeComHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotBotHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<WeComBotHost>(serviceProvider);
    }
}

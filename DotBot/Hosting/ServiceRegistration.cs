using DotBot.Configuration;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Gateway;
using DotBot.Localization;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.Modules;
using DotBot.Security;
using DotBot.Skills;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Hosting;

public static class ServiceRegistration
{
    public static IServiceCollection AddDotBot(
        this IServiceCollection services,
        AppConfig config,
        string workspacePath,
        string botPath)
    {
        services.AddSingleton(config);
        services.AddSingleton(new DotBotPaths
        {
            WorkspacePath = workspacePath,
            BotPath = botPath
        });
        services.AddSingleton(new PathBlacklist(config.Security.BlacklistedPaths));
        services.AddSingleton(new MemoryStore(botPath));
        services.AddSingleton(new SessionStore(botPath, config.CompactSessions));
        services.AddSingleton(new ApprovalStore(botPath));
        services.AddSingleton(new SkillsLoader(botPath));
        services.AddSingleton(new LanguageService(config.Language));

        var cronStorePath = Path.Combine(botPath, config.Cron.StorePath);
        services.AddSingleton(new CronService(cronStorePath));
        services.AddSingleton<CronTools>(sp => new CronTools(sp.GetRequiredService<CronService>()));

        services.AddSingleton<McpClientManager>();
        services.AddSingleton<SessionGate>();

        // Register configuration validation
        services.AddConfigurationValidation();

        if (config.DashBoard.Enabled)
        {
            var dashboardStoragePath = Path.Combine(botPath, "dashboard");
            var traceStore = new TraceStore(dashboardStoragePath);
            traceStore.LoadFromDisk();
            services.AddSingleton(traceStore);
            services.AddSingleton<TraceCollector>();

            var tokenUsageStore = new TokenUsageStore(dashboardStoragePath);
            tokenUsageStore.LoadFromDisk();
            services.AddSingleton(tokenUsageStore);
        }

        return services;
    }

    /// <summary>
    /// Creates and configures the module registry with automatic discovery.
    /// Uses source generator by default, falls back to reflection if needed.
    /// </summary>
    /// <returns>The configured module registry.</returns>
    public static ModuleRegistry CreateModuleRegistry()
    {
        return new ModuleRegistry();
    }

    /// <summary>
    /// Validates module configurations and prints diagnostics.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <param name="moduleRegistry">The module registry whose modules provide their own validators.</param>
    /// <returns>True if all configurations are valid.</returns>
    public static bool ValidateConfigurations(AppConfig config, ModuleRegistry moduleRegistry)
    {
        var validator = new ConfigValidator(moduleRegistry);
        return validator.ValidateAndLogErrors(config);
    }
}

/// <summary>
/// Extension methods for IServiceProvider.
/// </summary>
public static class ServiceProviderExtensions
{
    /// <summary>
    /// Initializes async services.
    /// </summary>
    public static async Task InitializeServicesAsync(this IServiceProvider provider)
    {
        var config = provider.GetRequiredService<AppConfig>();
        var mcpManager = provider.GetRequiredService<McpClientManager>();
        if (config.McpServers.Count > 0)
        {
            await mcpManager.ConnectAsync(config.McpServers);
        }
    }

    /// <summary>
    /// Disposes async services.
    /// </summary>
    public static async ValueTask DisposeServicesAsync(this IServiceProvider provider)
    {
        var cronService = provider.GetRequiredService<CronService>();
        cronService.Stop();
        cronService.Dispose();

        var mcpManager = provider.GetRequiredService<McpClientManager>();
        await mcpManager.DisposeAsync();
    }
}

using DotBot.Configuration;
using DotBot.Configuration.Core;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Localization;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.Modules.Registry;
using DotBot.Security;
using DotBot.Skills;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

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

        // Register module configurations
        services.AddModuleConfigurations(config);

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
    /// <param name="services">The service collection.</param>
    /// <param name="config">The application configuration.</param>
    /// <returns>True if all configurations are valid.</returns>
    public static bool ValidateConfigurations(this IServiceCollection services, AppConfig config)
    {
        var provider = new ModuleConfigProvider(config);
        provider.RegisterBinder<Configuration.Modules.QQModuleConfig, Configuration.Binders.QQModuleConfigBinder>();
        provider.RegisterBinder<Configuration.Modules.WeComModuleConfig, Configuration.Binders.WeComModuleConfigBinder>();
        provider.RegisterBinder<Configuration.Modules.ApiModuleConfig, Configuration.Binders.ApiModuleConfigBinder>();
        provider.RegisterBinder<Configuration.Modules.CliModuleConfig, Configuration.Binders.CliModuleConfigBinder>();

        var validationResults = provider.ValidateAll();
        if (validationResults.Count > 0)
        {
            foreach (var (section, errors) in validationResults)
            {
                foreach (var error in errors)
                {
                    AnsiConsole.MarkupLine($"[yellow][[Config]] Warning: {section} - {Markup.Escape(error)}[/]");
                }
            }
            return false;
        }
        return true;
    }

    extension(IServiceProvider provider)
    {
        public async Task InitializeServicesAsync()
        {
            var config = provider.GetRequiredService<AppConfig>();
            var mcpManager = provider.GetRequiredService<McpClientManager>();
            if (config.McpServers.Count > 0)
            {
                await mcpManager.ConnectAsync(config.McpServers);
            }
        }

        public async ValueTask DisposeServicesAsync()
        {
            var cronService = provider.GetRequiredService<CronService>();
            cronService.Stop();
            cronService.Dispose();

            var mcpManager = provider.GetRequiredService<McpClientManager>();
            await mcpManager.DisposeAsync();
        }
    }
}

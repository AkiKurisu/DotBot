using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Localization;
using DotBot.Mcp;
using DotBot.Memory;
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

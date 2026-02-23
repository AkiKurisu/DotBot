using System.Reflection;
using DotBot.Abstractions;
using DotBot.CLI;
using DotBot.Configuration;
using DotBot.Cron;
using DotBot.Mcp;
using DotBot.Modules;

namespace DotBot.Tools;

/// <summary>
/// Collects tool providers from modules and system sources.
/// </summary>
public static class ToolProviderCollector
{
    /// <summary>
    /// Collects tool providers from enabled modules and system providers.
    /// </summary>
    /// <param name="registry">The module registry.</param>
    /// <param name="config">The application configuration.</param>
    /// <param name="includeSystemProviders">Whether to include system providers (Cron, Mcp).</param>
    /// <returns>A list of tool providers.</returns>
    public static List<IAgentToolProvider> Collect(
        ModuleRegistry registry,
        AppConfig config,
        bool includeSystemProviders = true)
    {
        var providers = new List<IAgentToolProvider>();

        // Collect providers from all enabled modules
        foreach (var module in registry.GetEnabledModules(config))
        {
            providers.AddRange(module.GetToolProviders());
        }

        // Add system providers (these don't belong to any specific module)
        if (includeSystemProviders)
        {
            providers.Add(new CoreToolProvider());
            providers.Add(new CronToolProvider());
            providers.Add(new McpToolProvider());
        }

        return providers;
    }

    /// <summary>
    /// Scans assemblies for tool icons and registers them.
    /// Should be called once at application startup.
    /// </summary>
    /// <param name="assemblies">Assemblies to scan for tool icons.</param>
    public static void ScanToolIcons(params Assembly[] assemblies)
    {
        ToolIconRegistry.ScanAssemblies(assemblies);
    }
}

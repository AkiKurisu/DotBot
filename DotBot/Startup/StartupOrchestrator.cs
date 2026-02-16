using DotBot.Abstractions;
using DotBot.Hosting;
using DotBot.Modules.Registry;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DotBot.Startup;

/// <summary>
/// Orchestrates the startup sequence for DotBot application.
/// Handles module discovery, selection, and host creation.
/// </summary>
public sealed class StartupOrchestrator
{
    private readonly ModuleRegistry _registry;
    private readonly AppConfig _config;
    private readonly DotBotPaths _paths;

    /// <summary>
    /// Gets the module registry for external inspection.
    /// </summary>
    public ModuleRegistry Registry => _registry;

    /// <summary>
    /// Creates a new startup orchestrator.
    /// </summary>
    /// <param name="registry">The module registry containing all registered modules.</param>
    /// <param name="config">The application configuration.</param>
    /// <param name="paths">The workspace and bot paths.</param>
    public StartupOrchestrator(
        ModuleRegistry registry,
        AppConfig config,
        DotBotPaths paths)
    {
        _registry = registry;
        _config = config;
        _paths = paths;
    }

    /// <summary>
    /// Creates the service collection with all module services configured.
    /// </summary>
    /// <returns>The configured service collection.</returns>
    public IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        
        // Add core services
        services.AddSingleton(_config);
        services.AddSingleton(_paths);
        
        return services;
    }

    /// <summary>
    /// Configures services for the selected module.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="module">The module to configure services for.</param>
    public void ConfigureModuleServices(IServiceCollection services, IDotBotModule module)
    {
        var context = new ModuleContext
        {
            Config = _config,
            Paths = _paths
        };
        
        module.ConfigureServices(services, context);
    }

    /// <summary>
    /// Creates the host for the specified module.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="module">The module to create a host for.</param>
    /// <returns>The created host instance.</returns>
    public IDotBotHost CreateHost(IServiceProvider serviceProvider, IDotBotModule module)
    {
        var factory = _registry.GetHostFactory(module.Name);
        if (factory != null)
        {
            var context = new ModuleContext
            {
                Config = _config,
                Paths = _paths,
                ServiceProvider = serviceProvider
            };
            return factory.CreateHost(serviceProvider, context);
        }

        throw new InvalidOperationException($"No host factory registered for module '{module.Name}'");
    }

    /// <summary>
    /// Selects and creates the primary host based on enabled modules.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>A tuple containing the service provider and the host.</returns>
    public (IServiceProvider Provider, IDotBotHost Host) SelectAndCreateHost(IServiceCollection services)
    {
        // Print module diagnostics
        _registry.PrintDiagnostics(_config);
        
        // Select primary module
        var primaryModule = _registry.SelectPrimaryModule(_config);
        if (primaryModule == null)
        {
            throw new InvalidOperationException("No modules are enabled. Please enable at least one module in the configuration.");
        }

        AnsiConsole.MarkupLine($"[green][[Startup]][/] Using module: {primaryModule.Name}");

        // Configure services for the selected module
        ConfigureModuleServices(services, primaryModule);

        // Build service provider
        var provider = services.BuildServiceProvider();

        // Create host
        var host = CreateHost(provider, primaryModule);

        return (provider, host);
    }
}

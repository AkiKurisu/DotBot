using System.Reflection;
using DotBot.Abstractions;
using Spectre.Console;

namespace DotBot.Modules.Registry;

/// <summary>
/// Registry for managing DotBot modules.
/// Provides module discovery, registration, and selection capabilities.
/// Supports both source generator and reflection-based discovery.
/// </summary>
public sealed class ModuleRegistry
{
    private readonly List<IDotBotModule> _modules = [];
    private readonly Dictionary<string, IHostFactory> _hostFactories = new();
    private readonly string _discoveryMode;

    /// <summary>
    /// Gets the discovery mode used to find modules.
    /// </summary>
    public string DiscoveryMode => _discoveryMode;

    /// <summary>
    /// Gets all registered modules.
    /// </summary>
    public IReadOnlyList<IDotBotModule> Modules => _modules.AsReadOnly();

    /// <summary>
    /// Creates a new module registry and discovers modules.
    /// </summary>
    /// <param name="forceReflection">If true, forces reflection-based discovery even if source generator is available.</param>
    public ModuleRegistry(bool forceReflection = false)
    {
        if (forceReflection)
        {
            _discoveryMode = "Reflection (forced)";
            DiscoverModulesViaReflection();
        }
        else
        {
            // Try source generator first
            var provider = CreateSourceGeneratorProvider();
            if (provider != null)
            {
                _discoveryMode = "SourceGenerator";
                DiscoverModulesViaSourceGenerator(provider);
            }
            else
            {
                _discoveryMode = "Reflection (fallback)";
                DiscoverModulesViaReflection();
            }
        }
    }

    /// <summary>
    /// Attempts to create the source-generated registration provider.
    /// </summary>
    private static IModuleRegistrationProvider? CreateSourceGeneratorProvider()
    {
        try
        {
            // Look for the generated provider in the Generated namespace
            var assembly = Assembly.GetExecutingAssembly();
            var providerType = assembly.GetType("DotBot.Generated.ModuleRegistrationProvider");
            
            if (providerType != null && typeof(IModuleRegistrationProvider).IsAssignableFrom(providerType))
            {
                return Activator.CreateInstance(providerType) as IModuleRegistrationProvider;
            }
        }
        catch
        {
            // Source generator not available, fall back to reflection
        }

        return null;
    }

    /// <summary>
    /// Discovers modules using the source-generated provider.
    /// </summary>
    private void DiscoverModulesViaSourceGenerator(IModuleRegistrationProvider provider)
    {
        var registrations = provider.GetRegistrations();
        foreach (var registration in registrations)
        {
            if (registration.Module is IDotBotModule module)
            {
                _modules.Add(module);
            }
            if (registration.HostFactory is IHostFactory factory)
            {
                _hostFactories[registration.Name] = factory;
            }
        }
    }

    /// <summary>
    /// Discovers modules using reflection (fallback mode).
    /// </summary>
    private void DiscoverModulesViaReflection()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Find all types implementing IDotBotModule
        var moduleTypes = assembly.GetTypes()
            .Where(t => typeof(IDotBotModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var moduleType in moduleTypes)
        {
            try
            {
                if (Activator.CreateInstance(moduleType) is IDotBotModule module)
                {
                    _modules.Add(module);
                }
            }
            catch
            {
                // Skip modules that cannot be instantiated
            }
        }

        // Find all types implementing IHostFactory
        var factoryTypes = assembly.GetTypes()
            .Where(t => typeof(IHostFactory).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var factoryType in factoryTypes)
        {
            try
            {
                if (Activator.CreateInstance(factoryType) is IHostFactory factory)
                {
                    _hostFactories[factory.ModeName] = factory;
                }
            }
            catch
            {
                // Skip factories that cannot be instantiated
            }
        }
    }

    /// <summary>
    /// Registers a module with its optional host factory (manual registration).
    /// </summary>
    /// <param name="module">The module to register.</param>
    /// <param name="hostFactory">Optional host factory for the module.</param>
    public void RegisterModule(IDotBotModule module, IHostFactory? hostFactory = null)
    {
        _modules.Add(module);
        if (hostFactory != null)
        {
            _hostFactories[module.Name] = hostFactory;
        }
    }

    /// <summary>
    /// Gets all enabled modules based on the current configuration.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>List of enabled modules sorted by priority (highest first).</returns>
    public List<IDotBotModule> GetEnabledModules(AppConfig config)
    {
        return _modules
            .Where(m => m.IsEnabled(config))
            .OrderByDescending(m => m.Priority)
            .ToList();
    }

    /// <summary>
    /// Selects the primary module to run based on priority.
    /// When multiple modules are enabled, the highest priority module is selected.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>The selected module, or null if no modules are enabled.</returns>
    public IDotBotModule? SelectPrimaryModule(AppConfig config)
    {
        var enabledModules = GetEnabledModules(config);
        return enabledModules.FirstOrDefault();
    }

    /// <summary>
    /// Gets the host factory for a module.
    /// </summary>
    /// <param name="moduleName">The module name.</param>
    /// <returns>The host factory, or null if not found.</returns>
    public IHostFactory? GetHostFactory(string moduleName)
    {
        return _hostFactories.TryGetValue(moduleName, out var factory) ? factory : null;
    }

    /// <summary>
    /// Prints diagnostic information about registered and enabled modules.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    public void PrintDiagnostics(AppConfig config)
    {
        AnsiConsole.MarkupLine($"[grey][[ModuleRegistry]][/] [green]Module diagnostics (discovery: {_discoveryMode}):[/]");

        // Print all registered modules
        AnsiConsole.MarkupLine("[grey]  Registered modules:[/]");
        foreach (var module in _modules.OrderBy(m => m.Name))
        {
            var isEnabled = module.IsEnabled(config);
            var status = isEnabled ? "[green]enabled[/]" : "[grey]disabled[/]";
            AnsiConsole.MarkupLine($"[grey]    - {module.Name} (priority: {module.Priority}): {status}[/]");
        }

        // Print enabled modules
        var enabledModules = GetEnabledModules(config);
        if (enabledModules.Count > 0)
        {
            AnsiConsole.MarkupLine("[grey]  Enabled modules (sorted by priority):[/]");
            foreach (var module in enabledModules)
            {
                AnsiConsole.MarkupLine($"[grey]    - {module.Name} (priority: {module.Priority})[/]");
            }

            // Print selected primary module
            var primary = enabledModules.First();
            AnsiConsole.MarkupLine($"[green]  Primary module selected: {primary.Name}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]  No modules enabled - defaulting to CLI mode[/]");
        }
    }
}

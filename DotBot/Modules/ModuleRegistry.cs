using DotBot.Abstractions;
using DotBot.Configuration;

namespace DotBot.Modules;

/// <summary>
/// Registry for managing DotBot modules.
/// Provides module discovery, registration, and selection capabilities.
/// </summary>
public sealed partial class ModuleRegistry
{
    private readonly List<IDotBotModule> _modules = [];

    private readonly Dictionary<string, IHostFactory> _hostFactories = new();

    /// <summary>
    /// Gets all registered modules.
    /// </summary>
    public IReadOnlyList<IDotBotModule> Modules => _modules.AsReadOnly();

    /// <summary>
    /// Creates a new module registry and discovers modules.
    /// </summary>
    public ModuleRegistry()
    {
        RegisterSourceGeneratedModules();
    }

    // Partial method - will be implemented by source generator
    partial void RegisterSourceGeneratedModules();

    /// <summary>
    /// Registers a module and its optional host factory.
    /// Called by the source-generated partial method.
    /// </summary>
    private void AddModule(IDotBotModule module, IHostFactory? hostFactory)
    {
        _modules.Add(module);
        if (hostFactory != null)
        {
            _hostFactories[module.Name] = hostFactory;
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
        return _hostFactories.GetValueOrDefault(moduleName);
    }
}

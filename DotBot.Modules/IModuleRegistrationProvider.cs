namespace DotBot.Modules;

/// <summary>
/// Provides module registrations discovered by the source generator.
/// This interface is implemented by the generated ModuleRegistrationProvider class.
/// </summary>
public interface IModuleRegistrationProvider
{
    /// <summary>
    /// Gets all discovered module registrations.
    /// </summary>
    /// <returns>An enumerable of module registrations.</returns>
    System.Collections.Generic.IEnumerable<ModuleRegistration> GetRegistrations();
}

/// <summary>
/// Represents a discovered module registration.
/// </summary>
public sealed class ModuleRegistration
{
    /// <summary>
    /// Gets or sets the module instance.
    /// </summary>
    public object? Module { get; set; }

    /// <summary>
    /// Gets or sets the host factory instance, if available.
    /// </summary>
    public object? HostFactory { get; set; }

    /// <summary>
    /// Gets or sets the module name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the module priority.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the discovery source (e.g., "SourceGenerator" or "Reflection").
    /// </summary>
    public string DiscoverySource { get; set; } = string.Empty;
}

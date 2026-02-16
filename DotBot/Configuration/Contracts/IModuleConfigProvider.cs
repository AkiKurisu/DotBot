namespace DotBot.Configuration.Contracts;

/// <summary>
/// Provides access to module configurations.
/// Acts as a registry for all module configuration binders.
/// </summary>
public interface IModuleConfigProvider
{
    /// <summary>
    /// Gets the module configuration by type.
    /// </summary>
    /// <typeparam name="TConfig">The module configuration type.</typeparam>
    /// <returns>The module configuration, or null if not registered.</returns>
    TConfig? GetConfig<TConfig>() where TConfig : class;

    /// <summary>
    /// Gets all registered configuration section names.
    /// </summary>
    IReadOnlyList<string> GetRegisteredSections();

    /// <summary>
    /// Validates all module configurations.
    /// </summary>
    /// <returns>Dictionary of section name to validation errors.</returns>
    IReadOnlyDictionary<string, IReadOnlyList<string>> ValidateAll();
}

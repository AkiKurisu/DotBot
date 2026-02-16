namespace DotBot.Configuration.Contracts;

/// <summary>
/// Binds module-specific configuration from the application configuration.
/// Modules can implement this interface to define their own configuration schema.
/// </summary>
/// <typeparam name="TConfig">The module configuration type.</typeparam>
public interface IModuleConfigBinder<TConfig> where TConfig : class, new()
{
    /// <summary>
    /// Gets the section name in the configuration file.
    /// </summary>
    string SectionName { get; }

    /// <summary>
    /// Binds the configuration from AppConfig to the module-specific configuration.
    /// </summary>
    /// <param name="appConfig">The application configuration.</param>
    /// <returns>The bound module configuration.</returns>
    TConfig Bind(AppConfig appConfig);

    /// <summary>
    /// Validates the module configuration.
    /// </summary>
    /// <param name="config">The module configuration to validate.</param>
    /// <returns>List of validation errors, empty if valid.</returns>
    IReadOnlyList<string> Validate(TConfig config);
}

namespace DotBot.Configuration.Validators;

/// <summary>
/// Provides unified configuration validation for all modules.
/// </summary>
public sealed class ConfigValidator
{
    private readonly QQConfigValidator _qqValidator = new();
    private readonly WeComConfigValidator _weComValidator = new();
    private readonly ApiConfigValidator _apiValidator = new();

    /// <summary>
    /// Validates all module configurations.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>Dictionary of section name to validation errors.</returns>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ValidateAll(AppConfig config)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>();

        var qqErrors = _qqValidator.Validate(config.QQBot);
        if (qqErrors.Count > 0)
        {
            result["QQBot"] = qqErrors;
        }

        var weComErrors = _weComValidator.Validate(config.WeComBot);
        if (weComErrors.Count > 0)
        {
            result["WeComBot"] = weComErrors;
        }

        var apiErrors = _apiValidator.Validate(config.Api);
        if (apiErrors.Count > 0)
        {
            result["Api"] = apiErrors;
        }

        return result;
    }

    /// <summary>
    /// Validates all configurations and logs warnings for invalid settings.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>True if all configurations are valid.</returns>
    public bool ValidateAndLogErrors(AppConfig config)
    {
        var validationResults = ValidateAll(config);
        if (validationResults.Count > 0)
        {
            foreach (var (section, errors) in validationResults)
            {
                foreach (var error in errors)
                {
                    Console.WriteLine($"[Config] Warning: {section} - {error}");
                }
            }
            return false;
        }
        return true;
    }
}

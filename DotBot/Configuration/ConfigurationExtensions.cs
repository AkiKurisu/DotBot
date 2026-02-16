using DotBot.Configuration.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Configuration;

/// <summary>
/// Extension methods for configuration services.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds configuration validation services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddConfigurationValidation(this IServiceCollection services)
    {
        services.AddSingleton<ConfigValidator>();
        return services;
    }

    /// <summary>
    /// Validates all module configurations and logs warnings for invalid settings.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>True if all configurations are valid.</returns>
    public static bool ValidateAndLogErrors(this AppConfig config)
    {
        var validator = new ConfigValidator();
        return validator.ValidateAndLogErrors(config);
    }
}

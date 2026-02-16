using DotBot.Configuration.Binders;
using DotBot.Configuration.Contracts;
using DotBot.Configuration.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Configuration;

/// <summary>
/// Extension methods for registering module configurations.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds module configuration services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="appConfig">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddModuleConfigurations(
        this IServiceCollection services,
        AppConfig appConfig)
    {
        var provider = new ModuleConfigProvider(appConfig);

        // Register built-in module configuration binders
        provider.RegisterBinder<Modules.QQModuleConfig, QQModuleConfigBinder>();
        provider.RegisterBinder<Modules.WeComModuleConfig, WeComModuleConfigBinder>();
        provider.RegisterBinder<Modules.ApiModuleConfig, ApiModuleConfigBinder>();
        provider.RegisterBinder<Modules.CliModuleConfig, CliModuleConfigBinder>();

        // Register as singleton
        services.AddSingleton<IModuleConfigProvider>(provider);
        services.AddSingleton(provider);

        // Register individual module configs for direct injection
        services.AddSingleton(provider.GetConfig<Modules.QQModuleConfig>()!);
        services.AddSingleton(provider.GetConfig<Modules.WeComModuleConfig>()!);
        services.AddSingleton(provider.GetConfig<Modules.ApiModuleConfig>()!);
        services.AddSingleton(provider.GetConfig<Modules.CliModuleConfig>()!);

        return services;
    }

    /// <summary>
    /// Validates all module configurations and logs warnings for invalid settings.
    /// </summary>
    /// <param name="provider">The module configuration provider.</param>
    /// <returns>True if all configurations are valid.</returns>
    public static bool ValidateAndLogErrors(this ModuleConfigProvider provider)
    {
        var validationResults = provider.ValidateAll();
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

using DotBot.Configuration.Contracts;
using DotBot.Configuration.Modules;

namespace DotBot.Configuration.Binders;

/// <summary>
/// Binds CLI module configuration from AppConfig.
/// CLI is enabled when no other modules are enabled.
/// </summary>
public sealed class CliModuleConfigBinder : IModuleConfigBinder<CliModuleConfig>
{
    /// <inheritdoc />
    public string SectionName => "Cli";

    /// <inheritdoc />
    public CliModuleConfig Bind(AppConfig appConfig)
    {
        // CLI is enabled when no other modules are enabled
        var enabled = !appConfig.QQBot.Enabled && 
                      !appConfig.WeComBot.Enabled && 
                      !appConfig.Api.Enabled;

        return new CliModuleConfig
        {
            Enabled = enabled
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(CliModuleConfig config)
    {
        // CLI mode has no required configuration
        return [];
    }
}

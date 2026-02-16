namespace DotBot.Configuration.Modules;

/// <summary>
/// Configuration for CLI module.
/// CLI is the default fallback mode when no other modules are enabled.
/// </summary>
public sealed class CliModuleConfig
{
    /// <summary>
    /// Gets whether the CLI mode is enabled.
    /// CLI is enabled when no other modules are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

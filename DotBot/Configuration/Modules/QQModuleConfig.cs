namespace DotBot.Configuration.Modules;

/// <summary>
/// Configuration for QQ module.
/// Provides a strongly-typed configuration view for the QQ bot.
/// </summary>
public sealed class QQModuleConfig
{
    /// <summary>
    /// Gets or sets whether the QQ bot is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the host address for the WebSocket connection.
    /// </summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>
    /// Gets or sets the port for the WebSocket connection.
    /// </summary>
    public int Port { get; set; } = 6700;

    /// <summary>
    /// Gets or sets the access token for authentication.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of admin user IDs.
    /// </summary>
    public List<long> AdminUsers { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of whitelisted user IDs.
    /// </summary>
    public List<long> WhitelistedUsers { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of whitelisted group IDs.
    /// </summary>
    public List<long> WhitelistedGroups { get; set; } = [];

    /// <summary>
    /// Gets or sets the approval timeout in seconds.
    /// </summary>
    public int ApprovalTimeoutSeconds { get; set; } = 60;
}

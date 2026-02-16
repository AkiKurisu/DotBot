namespace DotBot.Configuration.Modules;

/// <summary>
/// Configuration for WeCom module.
/// Provides a strongly-typed configuration view for the WeCom bot.
/// </summary>
public sealed class WeComModuleConfig
{
    /// <summary>
    /// Gets or sets whether the WeCom bot is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the host to bind the HTTP server.
    /// </summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>
    /// Gets or sets the port to bind the HTTP server.
    /// </summary>
    public int Port { get; set; } = 9000;

    /// <summary>
    /// Gets or sets the list of admin user IDs.
    /// </summary>
    public List<string> AdminUsers { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of whitelisted user IDs.
    /// </summary>
    public List<string> WhitelistedUsers { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of whitelisted chat IDs.
    /// </summary>
    public List<string> WhitelistedChats { get; set; } = [];

    /// <summary>
    /// Gets or sets the approval timeout in seconds.
    /// </summary>
    public int ApprovalTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the list of bot configurations.
    /// </summary>
    public List<WeComRobotConfig> Robots { get; set; } = [];

    /// <summary>
    /// Gets or sets the default robot configuration.
    /// </summary>
    public WeComRobotConfig? DefaultRobot { get; set; }
}

/// <summary>
/// Configuration for a WeCom robot.
/// </summary>
public sealed class WeComRobotConfig
{
    /// <summary>
    /// Gets or sets the bot path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token from WeCom bot configuration.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the encoding AES key.
    /// </summary>
    public string AesKey { get; set; } = string.Empty;
}

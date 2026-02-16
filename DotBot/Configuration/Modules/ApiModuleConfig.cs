namespace DotBot.Configuration.Modules;

/// <summary>
/// Configuration for API module.
/// Provides a strongly-typed configuration view for the API server.
/// </summary>
public sealed class ApiModuleConfig
{
    /// <summary>
    /// Gets or sets whether the API server is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the host to bind the HTTP server.
    /// </summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>
    /// Gets or sets the port to bind the HTTP server.
    /// </summary>
    public int Port { get; set; } = 8080;

    /// <summary>
    /// Gets or sets the API key for authentication.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to auto-approve operations.
    /// </summary>
    public bool AutoApprove { get; set; } = true;

    /// <summary>
    /// Gets or sets the approval mode.
    /// </summary>
    public string ApprovalMode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the approval timeout in seconds.
    /// </summary>
    public int ApprovalTimeoutSeconds { get; set; } = 120;
}

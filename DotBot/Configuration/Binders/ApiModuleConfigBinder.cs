using DotBot.Configuration.Contracts;
using DotBot.Configuration.Modules;

namespace DotBot.Configuration.Binders;

/// <summary>
/// Binds API module configuration from AppConfig.
/// </summary>
public sealed class ApiModuleConfigBinder : IModuleConfigBinder<ApiModuleConfig>
{
    /// <inheritdoc />
    public string SectionName => "Api";

    /// <inheritdoc />
    public ApiModuleConfig Bind(AppConfig appConfig)
    {
        return new ApiModuleConfig
        {
            Enabled = appConfig.Api.Enabled,
            Host = appConfig.Api.Host,
            Port = appConfig.Api.Port,
            ApiKey = appConfig.Api.ApiKey,
            AutoApprove = appConfig.Api.AutoApprove,
            ApprovalMode = appConfig.Api.ApprovalMode,
            ApprovalTimeoutSeconds = appConfig.Api.ApprovalTimeoutSeconds
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(ApiModuleConfig config)
    {
        var errors = new List<string>();

        if (config.Enabled)
        {
            if (config.Port <= 0 || config.Port > 65535)
            {
                errors.Add($"Invalid port number: {config.Port}");
            }

            // Note: ApiKey is optional for API mode (can be empty for open access)
            if (!string.IsNullOrEmpty(config.ApiKey) && config.ApiKey.Length < 8)
            {
                errors.Add("API key should be at least 8 characters long for security");
            }
        }

        return errors;
    }
}

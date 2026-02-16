using DotBot.Configuration.Contracts;
using DotBot.Configuration.Modules;

namespace DotBot.Configuration.Binders;

/// <summary>
/// Binds QQ module configuration from AppConfig.
/// </summary>
public sealed class QQModuleConfigBinder : IModuleConfigBinder<QQModuleConfig>
{
    /// <inheritdoc />
    public string SectionName => "QQBot";

    /// <inheritdoc />
    public QQModuleConfig Bind(AppConfig appConfig)
    {
        return new QQModuleConfig
        {
            Enabled = appConfig.QQBot.Enabled,
            Host = appConfig.QQBot.Host,
            Port = appConfig.QQBot.Port,
            AccessToken = appConfig.QQBot.AccessToken,
            AdminUsers = [..appConfig.QQBot.AdminUsers],
            WhitelistedUsers = [..appConfig.QQBot.WhitelistedUsers],
            WhitelistedGroups = [..appConfig.QQBot.WhitelistedGroups],
            ApprovalTimeoutSeconds = appConfig.QQBot.ApprovalTimeoutSeconds
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(QQModuleConfig config)
    {
        var errors = new List<string>();

        if (config.Enabled && string.IsNullOrEmpty(config.AccessToken))
        {
            errors.Add("AccessToken is required when QQ bot is enabled");
        }

        if (config.Enabled && config.Port <= 0 || config.Port > 65535)
        {
            errors.Add($"Invalid port number: {config.Port}");
        }

        return errors;
    }
}

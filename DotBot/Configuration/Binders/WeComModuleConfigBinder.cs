using DotBot.Configuration.Contracts;
using DotBot.Configuration.Modules;

namespace DotBot.Configuration.Binders;

/// <summary>
/// Binds WeCom module configuration from AppConfig.
/// </summary>
public sealed class WeComModuleConfigBinder : IModuleConfigBinder<WeComModuleConfig>
{
    /// <inheritdoc />
    public string SectionName => "WeComBot";

    /// <inheritdoc />
    public WeComModuleConfig Bind(AppConfig appConfig)
    {
        return new WeComModuleConfig
        {
            Enabled = appConfig.WeComBot.Enabled,
            Host = appConfig.WeComBot.Host,
            Port = appConfig.WeComBot.Port,
            AdminUsers = [..appConfig.WeComBot.AdminUsers],
            WhitelistedUsers = [..appConfig.WeComBot.WhitelistedUsers],
            WhitelistedChats = [..appConfig.WeComBot.WhitelistedChats],
            ApprovalTimeoutSeconds = appConfig.WeComBot.ApprovalTimeoutSeconds,
            Robots = appConfig.WeComBot.Robots.Select(r => new WeComRobotConfig
            {
                Path = r.Path,
                Token = r.Token,
                AesKey = r.AesKey
            }).ToList(),
            DefaultRobot = appConfig.WeComBot.DefaultRobot != null 
                ? new WeComRobotConfig
                {
                    Path = appConfig.WeComBot.DefaultRobot.Path,
                    Token = appConfig.WeComBot.DefaultRobot.Token,
                    AesKey = appConfig.WeComBot.DefaultRobot.AesKey
                }
                : null
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(WeComModuleConfig config)
    {
        var errors = new List<string>();

        if (config.Enabled)
        {
            if (config.Port <= 0 || config.Port > 65535)
            {
                errors.Add($"Invalid port number: {config.Port}");
            }

            // Validate robot configurations
            foreach (var robot in config.Robots)
            {
                if (string.IsNullOrEmpty(robot.Token))
                {
                    errors.Add($"Robot at path '{robot.Path}' is missing Token");
                }
            }

            if (config.DefaultRobot != null && string.IsNullOrEmpty(config.DefaultRobot.Token))
            {
                errors.Add("Default robot is missing Token");
            }
        }

        return errors;
    }
}

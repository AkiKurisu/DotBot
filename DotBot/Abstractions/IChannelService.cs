using DotBot.Cron;
using DotBot.Heartbeat;
using DotBot.Security;

namespace DotBot.Abstractions;

/// <summary>
/// Represents a channel service that handles communication for a specific platform
/// (e.g., QQ, WeCom, API). Used by GatewayHost to run multiple channels concurrently.
/// </summary>
public interface IChannelService : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique name of this channel (e.g., "qq", "wecom", "api").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The shared HeartbeatService injected by GatewayHost before the channel starts.
    /// Allows slash commands (/heartbeat) to operate within this channel.
    /// </summary>
    HeartbeatService? HeartbeatService { get; set; }

    /// <summary>
    /// The shared CronService injected by GatewayHost before the channel starts.
    /// Allows slash commands (/cron) to operate within this channel.
    /// </summary>
    CronService? CronService { get; set; }

    /// <summary>
    /// The channel-specific approval service, if any.
    /// Used by GatewayHost to route background-task approvals back to the originating channel.
    /// </summary>
    IApprovalService? ApprovalService { get; }

    /// <summary>
    /// Starts the channel service. This is a long-running task that completes
    /// only when the channel is stopped or the cancellation token is triggered.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the channel service gracefully.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Delivers a message to a specific target within this channel.
    /// Used by GatewayHost for cross-channel routing (Cron results, Heartbeat notifications).
    /// </summary>
    /// <param name="target">The target identifier (e.g., user ID, group ID, chat ID).</param>
    /// <param name="content">The message content to deliver.</param>
    Task DeliverMessageAsync(string target, string content);
}

using DotBot.Abstractions;
using DotBot.Configuration;
using Spectre.Console;

namespace DotBot.Gateway;

/// <summary>
/// Routes messages from shared infrastructure (Cron, Heartbeat) to the appropriate channel service.
/// </summary>
public sealed class MessageRouter : IMessageRouter
{
    private readonly AppConfig _config;
    private readonly Dictionary<string, IChannelService> _channels = new(StringComparer.OrdinalIgnoreCase);

    public MessageRouter(AppConfig config)
    {
        _config = config;
    }

    public void RegisterChannel(IChannelService service)
    {
        _channels[service.Name] = service;
    }

    public async Task DeliverAsync(string channel, string target, string content)
    {
        if (_channels.TryGetValue(channel, out var service))
        {
            try
            {
                await service.DeliverMessageAsync(target, content);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(
                    $"[grey][[Gateway]][/] [red]Delivery to {channel}/{target} failed: {Markup.Escape(ex.Message)}[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[grey][[Gateway]][/] [yellow]No channel registered for '{Markup.Escape(channel)}', skipping delivery[/]");
        }
    }

    public async Task BroadcastToAdminsAsync(string content)
    {
        // Notify QQ admins
        if (_channels.TryGetValue("qq", out var qqChannel))
        {
            foreach (var adminId in _config.QQBot.AdminUsers)
            {
                try
                {
                    await qqChannel.DeliverMessageAsync(adminId.ToString(), content);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(
                        $"[grey][[Gateway]][/] [red]QQ admin {adminId} notify failed: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }

        // Notify via WeCom webhook (broadcast to group)
        if (_channels.TryGetValue("wecom", out var wecomChannel) &&
            _config.Heartbeat.NotifyAdmin)
        {
            try
            {
                await wecomChannel.DeliverMessageAsync("", content);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(
                    $"[grey][[Gateway]][/] [red]WeCom notify failed: {Markup.Escape(ex.Message)}[/]");
            }
        }
    }
}

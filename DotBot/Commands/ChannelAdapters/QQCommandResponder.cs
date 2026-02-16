using DotBot.Commands.Core;
using DotBot.QQ;
using DotBot.QQ.OneBot;

namespace DotBot.Commands.ChannelAdapters;

/// <summary>
/// QQ channel implementation of ICommandResponder.
/// </summary>
public sealed class QQCommandResponder(QQBotClient client, OneBotMessageEvent evt) : ICommandResponder
{
    /// <inheritdoc />
    public Task SendTextAsync(string message)
    {
        return client.SendMessageAsync(evt, message);
    }
    
    /// <inheritdoc />
    public Task SendMarkdownAsync(string markdown)
    {
        // QQ doesn't support markdown, send as plain text
        return client.SendMessageAsync(evt, markdown);
    }
}

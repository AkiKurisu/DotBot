using DotBot.QQ;

namespace DotBot.Context;

/// <summary>
/// Builds the [Runtime Context] block that is appended to each user message.
/// Keeping dynamic values (time, per-message sender) out of the system prompt
/// ensures the system prompt prefix stays stable across requests, enabling
/// LLM prompt cache reuse.
/// </summary>
public static class RuntimeContextBuilder
{
    /// <summary>
    /// Appends a [Runtime Context] block to the given prompt containing:
    /// - Current time (always)
    /// - QQ Group sender info (only for group messages, where the session is shared
    ///   across all group members and the sender changes per message)
    /// </summary>
    public static string AppendTo(string prompt)
    {
        var lines = new List<string>();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm (dddd)");
        lines.Add($"Current Time: {now} ({TimeZoneInfo.Local.DisplayName})");

        // QQ Group: session key is qq_{GroupId}, shared across all group members.
        // Sender info is dynamic (different per message) so it cannot live in the
        // system prompt without busting the cache on every new sender.
        // QQ Private and WeCom sessions are per-person — sender info stays stable
        // in the system prompt and needs no injection here.
        if (QQChatContextScope.Current is { IsGroupMessage: true } qq)
        {
            lines.Add($"Sender QQ: {qq.UserId}");
            lines.Add($"Sender Name: {qq.SenderName}");
        }

        return $"{prompt}\n\n[Runtime Context]\n{string.Join("\n", lines)}";
    }
}

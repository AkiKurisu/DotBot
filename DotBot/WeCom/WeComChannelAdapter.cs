using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotBot.CLI;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Heartbeat;
using DotBot.Memory;
using DotBot.Security;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotBot.WeCom;

/// <summary>
/// WeCom channel adapter - bridges WeCom messages to the DotBot Agent.
/// </summary>
public sealed class WeComChannelAdapter : IAsyncDisposable
{
    private readonly AIAgent _agent;
    
    private readonly SessionStore _sessionStore;
    
    private readonly HeartbeatService? _heartbeatService;
    
    private readonly CronService? _cronService;
    
    private readonly WeComPermissionService _permissionService;
    
    private readonly WeComApprovalService _approvalService;

    private readonly TraceCollector? _traceCollector;

    private readonly TokenUsageStore? _tokenUsageStore;

    public WeComChannelAdapter(
        AIAgent agent,
        SessionStore sessionStore,
        WeComBotRegistry registry,
        WeComPermissionService permissionService,
        WeComApprovalService approvalService,
        HeartbeatService? heartbeatService = null,
        CronService? cronService = null,
        TraceCollector? traceCollector = null,
        TokenUsageStore? tokenUsageStore = null)
    {
        _agent = agent;
        _sessionStore = sessionStore;
        _heartbeatService = heartbeatService;
        _cronService = cronService;
        _permissionService = permissionService;
        _approvalService = approvalService;
        _traceCollector = traceCollector;
        _tokenUsageStore = tokenUsageStore;

        // Attach handlers to all registered bot paths
        foreach (var path in registry.GetAllPaths())
        {
            registry.SetHandlers(path,
                textHandler: HandleTextMessageAsync,
                commonHandler: HandleCommonMessageAsync,
                eventHandler: HandleEventMessageAsync);
            AnsiConsole.MarkupLine($"[grey][[WeCom]][/] [green]Registered handler for:[/] {Markup.Escape(path)}");
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    #region Message Handlers

    private async Task HandleTextMessageAsync(string[] parameters, WeComFrom from, IWeComPusher pusher)
    {
        var plainText = string.Join(" ", parameters).Trim();
        if (string.IsNullOrEmpty(plainText))
        {
            await pusher.PushTextAsync("请输入消息内容");
            return;
        }

        var chatId = pusher.GetChatId();
        var sessionId = $"wecom_{chatId}_{from.UserId}";

        LogIncoming("text", chatId, $"{from.Name} (uid={from.UserId})", plainText);

        // Try to handle approval reply first
        if (_approvalService.TryHandleApprovalReply(plainText, from.UserId))
        {
            LogIncoming("approval", chatId, from.Name, $"审批回复: {plainText}");
            return;
        }

        // Handle slash commands
        if (await HandleCommandAsync(pusher, plainText, sessionId))
            return;

        // Get user role from permission service
        var userRole = _permissionService.GetUserRole(from.UserId, chatId);
        var roleString = userRole switch
        {
            WeComUserRole.Admin => "Admin",
            WeComUserRole.Whitelisted => "Whitelisted",
            _ => "User"
        };

        var approvalContext = new ApprovalContext
        {
            UserId = from.UserId,
            UserRole = roleString,
            GroupId = 0,
            Source = ApprovalSource.WeCom
        };

        try
        {
            using var _ = ApprovalContextScope.Set(approvalContext);
            using var __ = WeComPusherScope.Set(pusher);
            using var ___ = WeComChatContextScope.Set(new WeComChatContext
            {
                ChatId = chatId,
                UserId = from.UserId,
                UserName = from.Name
            });

            var session = await _sessionStore.LoadOrCreateAsync(_agent, sessionId, CancellationToken.None);

            var textBuffer = new StringBuilder();
            long inputTokens = 0, outputTokens = 0, totalTokens = 0;
            var toolTimers = new Dictionary<string, Stopwatch>();
            var toolNameMap = new Dictionary<string, string>();

            _traceCollector?.RecordSessionMetadata(
                sessionId,
                null,
                ToolIconRegistry.GetAllToolIcons().Keys);
            _traceCollector?.RecordRequest(sessionId, plainText);

            TracingChatClient.CurrentSessionKey = sessionId;
            TracingChatClient.ResetCallState(sessionId);
            try
            {
                await foreach (var update in _agent.RunStreamingAsync(plainText, session))
                {
                    foreach (var content in update.Contents)
                    {
                        switch (content)
                        {
                            case FunctionCallContent functionCall:
                                await FlushTextBufferAsync(pusher, textBuffer);

                                var icon = ToolIconRegistry.GetToolIcon(functionCall.Name);
                                var toolNotice = $"{icon} 正在调用: {functionCall.Name}...";
                                await pusher.PushTextAsync(toolNotice);
                                LogOutgoing(chatId, toolNotice);
                                LogToolCall(functionCall.Name, functionCall.Arguments);
                                _traceCollector?.RecordToolCallStarted(sessionId, functionCall);
                                if (!string.IsNullOrEmpty(functionCall.CallId))
                                {
                                    toolTimers[functionCall.CallId] = Stopwatch.StartNew();
                                    toolNameMap[functionCall.CallId] = functionCall.Name;
                                }
                                break;

                            case FunctionResultContent fr:
                                LogToolResult(fr.Result?.ToString());
                                double durationMs = 0;
                                if (!string.IsNullOrEmpty(fr.CallId) && toolTimers.TryGetValue(fr.CallId, out var sw))
                                {
                                    sw.Stop();
                                    durationMs = sw.Elapsed.TotalMilliseconds;
                                    toolTimers.Remove(fr.CallId);
                                }
                                string? toolName = null;
                                if (!string.IsNullOrEmpty(fr.CallId))
                                    toolNameMap.TryGetValue(fr.CallId, out toolName);
                                _traceCollector?.RecordToolCallCompleted(sessionId, fr, toolName, durationMs);
                                break;

                            case UsageContent usage:
                                if (usage.Details.InputTokenCount.HasValue)
                                    inputTokens = usage.Details.InputTokenCount.Value;
                                if (usage.Details.OutputTokenCount.HasValue)
                                    outputTokens = usage.Details.OutputTokenCount.Value;
                                if (usage.Details.TotalTokenCount.HasValue)
                                    totalTokens = usage.Details.TotalTokenCount.Value;
                                break;
                        }
                    }

                    if (!string.IsNullOrEmpty(update.Text))
                        textBuffer.Append(update.Text);
                }
            }
            finally
            {
                TracingChatClient.ResetCallState(sessionId);
                TracingChatClient.CurrentSessionKey = null;
            }

            if (totalTokens == 0 && (inputTokens > 0 || outputTokens > 0))
                totalTokens = inputTokens + outputTokens;


            var responseText = textBuffer.ToString();

            if (totalTokens > 0)
                textBuffer.Append($"\n\n[↑ {inputTokens} input ↓ {outputTokens} output]");

            await FlushTextBufferAsync(pusher, textBuffer);
            _traceCollector?.RecordResponse(sessionId, string.IsNullOrWhiteSpace(responseText) ? null : responseText);

            if (totalTokens > 0)
            {
                _traceCollector?.RecordTokenUsage(sessionId, inputTokens, outputTokens);

                _tokenUsageStore?.Record(new TokenUsageRecord
                {
                    Source = TokenUsageSource.WeCom,
                    UserId = from.UserId,
                    DisplayName = from.Name,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens
                });
            }

            await _sessionStore.SaveAsync(_agent, session, sessionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogError(from.Name, ex.Message);
            try
            {
                await pusher.PushTextAsync($"[Error] {ex.Message}");
            }
            catch
            {
                // ignored
            }
        }
    }

    private async Task HandleCommonMessageAsync(WeComMessage message, IWeComPusher pusher)
    {
        var info = $"收到 {message.MsgType} 类型消息";

        switch (message.MsgType)
        {
            case WeComMsgType.Image:
                info += $"\n图片URL: {message.Image?.ImageUrl}";
                LogIncoming("image", message.ChatId, message.From?.Name ?? "unknown", "发送了图片");
                break;
            case WeComMsgType.Attachment:
                info += $"\nCallbackId: {message.Attachment?.CallbackId}";
                LogIncoming("attachment", message.ChatId, message.From?.Name ?? "unknown", "发送了附件");
                break;
            case WeComMsgType.Mixed:
                info += $"\n包含 {message.MixedMessage?.MsgItems.Count ?? 0} 个项目";
                LogIncoming("mixed", message.ChatId, message.From?.Name ?? "unknown", "发送了图文混排");
                break;
            case WeComMsgType.Voice:
                info += $"\n语音转文本: {message.Voice?.Content}";
                LogIncoming("voice", message.ChatId, message.From?.Name ?? "unknown", "发送了语音");
                break;
            case WeComMsgType.File:
                info += $"\n文件URL: {message.File?.Url}";
                LogIncoming("file", message.ChatId, message.From?.Name ?? "unknown", "发送了文件");
                break;
        }

        await pusher.PushTextAsync(info);
    }

    private async Task<string?> HandleEventMessageAsync(string eventType, string chatType, WeComFrom from,
        IWeComPusher pusher)
    {
        var message = eventType switch
        {
            WeComEventType.AddToChat =>
                $"欢迎 {from.Name} 将我添加到{(chatType == WeComChatType.Group ? "群聊" : "会话")}！输入 /help 查看可用命令。",
            WeComEventType.EnterChat => $"你好，{from.Name}！我是 DotBot，随时为您服务。输入 /help 查看可用命令。",
            WeComEventType.DeleteFromChat => "再见！",
            _ => null
        };

        if (message != null)
        {
            LogIncoming("event", pusher.GetChatId(), from.Name, eventType);
            await pusher.PushTextAsync(message);
            LogOutgoing(pusher.GetChatId(), message);
        }

        return null; // Already replied via pusher
    }

    #endregion

    #region Commands

    private async Task<bool> HandleCommandAsync(IWeComPusher pusher, string text, string sessionId)
    {
        var cmd = text.Trim().ToLowerInvariant();

        switch (cmd)
        {
            case "/new" or "/clear":
            {
                _sessionStore.Delete(sessionId);
                await pusher.PushTextAsync("会话已清除，开始新的对话。");
                AnsiConsole.MarkupLine(
                    $"[grey][[WeCom]][/] [green]Session cleared:[/] {Markup.Escape(sessionId)}");
                return true;
            }

            case "/debug":
            {
                // Get user info from context
                var context = WeComChatContextScope.Current;
                if (context == null)
                {
                    await pusher.PushTextAsync("⚠️ 无法获取用户信息。");
                    return true;
                }

                var userRole = _permissionService.GetUserRole(context.UserId, pusher.GetChatId());
                if (userRole != WeComUserRole.Admin)
                {
                    await pusher.PushTextAsync("⚠️ 此命令仅管理员可用。");
                    return true;
                }

                var newState = DebugModeService.Toggle();
                var statusMsg = newState ? "✅ 调试模式已开启" : "✅ 调试模式已关闭";
                await pusher.PushTextAsync(statusMsg);
                
                AnsiConsole.MarkupLine($"[grey][[WeCom]][/] [yellow]Debug mode {(newState ? "enabled" : "disabled")}[/] by [green]{Markup.Escape(context.UserName)}[/] (uid={context.UserId})");
                return true;
            }

            case "/help":
            {
                var helpText = "可用命令：\n"
                               + "/new 或 /clear - 清除当前会话\n"
                               + "/debug - 切换调试模式（仅管理员）\n"
                               + "/heartbeat trigger - 立即触发心跳\n"
                               + "/cron list - 列出定时任务\n"
                               + "/cron remove <id> - 删除定时任务\n"
                               + "/help - 显示此帮助信息\n\n"
                               + "直接输入问题即可与 DotBot 对话。";
                await pusher.PushTextAsync(helpText);
                return true;
            }
        }

        if (cmd.StartsWith("/heartbeat"))
        {
            await HandleHeartbeatAsync(pusher, cmd);
            return true;
        }

        if (cmd.StartsWith("/cron"))
        {
            await HandleCronAsync(pusher, cmd);
            return true;
        }

        if (cmd.StartsWith("/"))
        {
            var msg = CommandHelper.FormatUnknownCommandMessage(text, KnownCommands);
            await pusher.PushTextAsync(msg);
            return true;
        }

        return false;
    }

    private static readonly string[] KnownCommands =
    [
        "/new", "/clear", "/debug", "/help", "/heartbeat", "/cron"
    ];

    private async Task HandleHeartbeatAsync(IWeComPusher pusher, string cmd)
    {
        if (_heartbeatService == null)
        {
            await pusher.PushTextAsync("心跳服务未启用。");
            return;
        }

        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 1 ? parts[1] : "trigger";

        if (subCmd == "trigger")
        {
            await pusher.PushTextAsync("正在触发心跳...");
            var result = await _heartbeatService.TriggerNowAsync();
            if (result != null)
                await pusher.PushTextAsync($"心跳结果：\n{result}");
            else
                await pusher.PushTextAsync("没有心跳响应（HEARTBEAT.md 可能为空或不存在）。");
        }
        else
        {
            await pusher.PushTextAsync("用法：/heartbeat trigger");
        }
    }

    private async Task HandleCronAsync(IWeComPusher pusher, string cmd)
    {
        if (_cronService == null)
        {
            await pusher.PushTextAsync("定时任务服务未启用。");
            return;
        }

        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 1 ? parts[1] : "list";

        switch (subCmd)
        {
            case "list":
            {
                var jobs = _cronService.ListJobs(includeDisabled: true);
                if (jobs.Count == 0)
                {
                    await pusher.PushTextAsync("没有定时任务。");
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"定时任务列表 ({jobs.Count})：");
                foreach (var job in jobs)
                {
                    var status = job.Enabled ? "启用" : "禁用";
                    var schedDesc = job.Schedule.Kind switch
                    {
                        "at" when job.Schedule.AtMs.HasValue =>
                            $"一次性 {DateTimeOffset.FromUnixTimeMilliseconds(job.Schedule.AtMs.Value):u}",
                        "every" when job.Schedule.EveryMs.HasValue =>
                            $"每 {TimeSpan.FromMilliseconds(job.Schedule.EveryMs.Value)}",
                        _ => job.Schedule.Kind
                    };
                    var next = job.State.NextRunAtMs.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(job.State.NextRunAtMs.Value).ToString("u")
                        : "-";
                    sb.AppendLine($"[{job.Id}] {job.Name} ({status})");
                    sb.AppendLine($"  计划: {schedDesc}");
                    sb.AppendLine($"  下次: {next}");
                }

                await pusher.PushTextAsync(sb.ToString().TrimEnd());
                break;
            }
            case "remove":
            {
                if (parts.Length < 3)
                {
                    await pusher.PushTextAsync("用法：/cron remove <jobId>");
                    break;
                }

                var jobId = parts[2];
                if (_cronService.RemoveJob(jobId))
                    await pusher.PushTextAsync($"任务 '{jobId}' 已删除。");
                else
                    await pusher.PushTextAsync($"任务 '{jobId}' 未找到。");
                break;
            }
            default:
                await pusher.PushTextAsync("用法：/cron list | /cron remove <id>");
                break;
        }
    }

    #endregion

    #region Helpers

    private static async Task FlushTextBufferAsync(IWeComPusher pusher, StringBuilder buffer)
    {
        var text = buffer.ToString().Trim();
        buffer.Clear();
        if (string.IsNullOrEmpty(text))
            return;

        await pusher.PushMarkdownAsync(text);
        LogOutgoing(pusher.GetChatId(), text);
    }

    #endregion

    #region Logging

    private static void LogIncoming(string type, string chatId, string sender, string text)
    {
        var tag = $"[cyan]Chat {Markup.Escape(chatId)}[/]";
        var preview = text.Length > 80 ? text[..80] + "..." : text;
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/] {tag} [green]{Markup.Escape(sender)}[/] ({type}): {Markup.Escape(preview)}");
    }

    private static void LogOutgoing(string chatId, string text)
    {
        var tag = $"[cyan]Chat {Markup.Escape(chatId)}[/]";
        var preview = text.Length > 80 ? text[..80] + "..." : text;
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/] {tag} [blue]DotBot[/]: {Markup.Escape(preview)}");
    }

    private static void LogError(string sender, string error)
    {
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/] [red]Error[/] processing message from [green]{Markup.Escape(sender)}[/]: {Markup.Escape(error)}");
    }

    private static void LogToolCall(string name, IDictionary<string, object?>? args)
    {
        var icon = ToolIconRegistry.GetToolIcon(name);
        var argsStr = string.Empty;
        if (args != null)
        {
            try
            {
                argsStr = JsonSerializer.Serialize(args, new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                argsStr = args.ToString() ?? string.Empty;
            }
        }

        var displayArgs = DebugModeService.IsEnabled() 
            ? argsStr 
            : (argsStr.Length > 150 ? argsStr[..150] + "..." : argsStr);
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/] [yellow]{Markup.Escape($"{icon} {name}")}[/] [dim]{Markup.Escape(displayArgs)}[/]");
    }

    private static void LogToolResult(string? result)
    {
        var text = result ?? "(no output)";
        var preview = text.Length > 200 ? text[..200] + "..." : text;
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/] [grey]Result: {Markup.Escape(preview)}[/]");
    }

    #endregion
}

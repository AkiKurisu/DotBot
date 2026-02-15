using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotBot.Agents;
using DotBot.CLI;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Heartbeat;
using DotBot.Memory;
using DotBot.QQ.OneBot;
using DotBot.Security;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotBot.QQ;

public sealed class QQChannelAdapter : IAsyncDisposable
{
    private readonly QQBotClient _client;
    
    private readonly AIAgent _agent;
    
    private readonly SessionStore _sessionStore;

    private readonly QQPermissionService _permissionService;

    private readonly QQApprovalService? _approvalService;

    private readonly HeartbeatService? _heartbeatService;

    private readonly CronService? _cronService;

    private readonly AgentFactory? _agentFactory;

    private readonly TraceCollector? _traceCollector;

    private readonly TokenUsageStore? _tokenUsageStore;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    
    public QQChannelAdapter(
        QQBotClient client,
        AIAgent agent,
        SessionStore sessionStore,
        QQPermissionService permissionService,
        QQApprovalService? approvalService = null,
        HeartbeatService? heartbeatService = null,
        CronService? cronService = null,
        AgentFactory? agentFactory = null,
        TraceCollector? traceCollector = null,
        TokenUsageStore? tokenUsageStore = null)
    {
        _client = client;
        _agent = agent;
        _sessionStore = sessionStore;
        _permissionService = permissionService;
        _approvalService = approvalService;
        _heartbeatService = heartbeatService;
        _cronService = cronService;
        _agentFactory = agentFactory;
        _traceCollector = traceCollector;
        _tokenUsageStore = tokenUsageStore;

        _client.OnGroupMessage += HandleGroupMessageAsync;
        _client.OnPrivateMessage += HandlePrivateMessageAsync;
    }

    public ValueTask DisposeAsync()
    {
        return _client.DisposeAsync();
    }

    private async Task HandleGroupMessageAsync(OneBotMessageEvent evt)
    {
        var plainText = evt.GetPlainText().Trim();
        if (string.IsNullOrEmpty(plainText))
            return;

        if (_approvalService != null && _approvalService.TryHandleApprovalReply(evt))
            return;

        var selfId = evt.SelfId;
        var isAtSelf = false;
        foreach (var seg in evt.Message)
        {
            var atQQ = seg.GetAtQQ();
            if (atQQ != null && atQQ == selfId.ToString())
            {
                isAtSelf = true;
                break;
            }
        }

        if (!isAtSelf)
            return;

        var role = _permissionService.GetUserRole(evt.UserId, evt.GroupId);
        if (role == QQUserRole.Unauthorized)
        {
            LogUnauthorized("group", evt.GroupId.ToString(), evt.Sender.DisplayName);
            return;
        }

        LogIncoming("group", evt.GroupId.ToString(), evt.Sender.DisplayName, plainText);
        await ProcessMessageAsync(evt, plainText, role);
    }

    private async Task HandlePrivateMessageAsync(OneBotMessageEvent evt)
    {
        var plainText = evt.GetPlainText().Trim();
        if (string.IsNullOrEmpty(plainText))
            return;

        if (_approvalService != null && _approvalService.TryHandleApprovalReply(evt))
            return;

        var role = _permissionService.GetUserRole(evt.UserId);
        if (role == QQUserRole.Unauthorized)
        {
            LogUnauthorized("private", evt.UserId.ToString(), evt.Sender.DisplayName);
            return;
        }

        LogIncoming("private", evt.UserId.ToString(), evt.Sender.DisplayName, plainText);
        await ProcessMessageAsync(evt, plainText, role);
    }

    private async Task ProcessMessageAsync(OneBotMessageEvent evt, string plainText, QQUserRole role)
    {
        var sessionId = $"qq_{evt.GetSessionId()}";

        if (await HandleCommandAsync(evt, plainText, sessionId))
            return;

        var approvalContext = new ApprovalContext
        {
            UserId = evt.UserId.ToString(),
            UserRole = role.ToString(),
            GroupId = evt.IsGroupMessage ? evt.GroupId : 0,
            Source = ApprovalSource.QQ
        };

        var chatContext = new QQChatContext
        {
            IsGroupMessage = evt.IsGroupMessage,
            GroupId = evt.GroupId,
            UserId = evt.UserId,
            SenderName = evt.Sender.DisplayName
        };

        try
        {
            using var _ = ApprovalContextScope.Set(approvalContext);
            using var __ = QQChatContextScope.Set(chatContext);

            var session = await _sessionStore.LoadOrCreateAsync(_agent, sessionId, CancellationToken.None);

            var textBuffer = new StringBuilder();
            long inputTokens = 0, outputTokens = 0, totalTokens = 0;
            var tokenTracker = _agentFactory?.GetOrCreateTokenTracker(sessionId);
            var toolTimers = new Dictionary<string, Stopwatch>();
            var toolNameMap = new Dictionary<string, string>();

            _traceCollector?.RecordSessionMetadata(
                sessionId,
                null,
                _agentFactory?.LastCreatedTools?.Select(t => t.Name));
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
                                await FlushTextBufferAsync(evt, textBuffer);

                                var icon = ToolIconRegistry.GetToolIcon(functionCall.Name);
                                var toolNotice = $"{icon} 正在调用: {functionCall.Name}...";
                                await _client.SendMessageAsync(evt, toolNotice);
                                LogOutgoing(evt, toolNotice);

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
            {
                tokenTracker?.Update(inputTokens, outputTokens);
                var displayInput = tokenTracker?.LastInputTokens ?? inputTokens;
                var displayOutput = tokenTracker?.TotalOutputTokens ?? outputTokens;
                textBuffer.Append($"\n\n[↑ {displayInput} input ↓ {displayOutput} output]");
            }

            await FlushTextBufferAsync(evt, textBuffer);
            _traceCollector?.RecordResponse(sessionId, string.IsNullOrWhiteSpace(responseText) ? null : responseText);

            if (totalTokens > 0)
            {
                _traceCollector?.RecordTokenUsage(sessionId, inputTokens, outputTokens);

                _tokenUsageStore?.Record(new TokenUsageRecord
                {
                    Source = evt.IsGroupMessage ? TokenUsageSource.QQGroup : TokenUsageSource.QQPrivate,
                    UserId = evt.UserId.ToString(),
                    DisplayName = evt.Sender.DisplayName,
                    GroupId = evt.IsGroupMessage ? evt.GroupId : 0,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens
                });
            }

            if (_agentFactory is { Compactor: not null, MaxContextTokens: > 0 } &&
                inputTokens >= _agentFactory.MaxContextTokens)
            {
                AnsiConsole.MarkupLine($"[grey][[QQ]][/] [yellow]Context compacting for session {Markup.Escape(sessionId)}...[/]");
                await _client.SendMessageAsync(evt, "⚠️ 上下文过长，正在压缩历史对话...");
                if (await _agentFactory.Compactor.TryCompactAsync(session))
                {
                    tokenTracker?.Reset();
                    _traceCollector?.RecordContextCompaction(sessionId);
                    await _client.SendMessageAsync(evt, "✅ 上下文压缩完成，可以继续对话。");
                }
            }

            await _sessionStore.SaveAsync(_agent, session, sessionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogError(evt.Sender.DisplayName, ex.Message);
            try
            {
                await _client.SendMessageAsync(evt, $"[Error] {ex.Message}");
            }
            catch
            {
                // ignored
            }
        }
    }

    private async Task FlushTextBufferAsync(OneBotMessageEvent evt, StringBuilder buffer)
    {
        var text = buffer.ToString().Trim();
        buffer.Clear();
        if (string.IsNullOrEmpty(text))
            return;
        await _client.SendMessageAsync(evt, text);
        LogOutgoing(evt, text);
    }

    private async Task<bool> HandleCommandAsync(OneBotMessageEvent evt, string text, string sessionId)
    {
        var cmd = text.Trim().ToLowerInvariant();

        switch (cmd)
        {
            case "/new" or "/clear":
            {
                _sessionStore.Delete(sessionId);
                _agentFactory?.RemoveTokenTracker(sessionId);
                await _client.SendMessageAsync(evt, "会话已清除，开始新的对话。");
                AnsiConsole.MarkupLine($"[grey][[QQ]][/] [green]Session cleared:[/] {Markup.Escape(sessionId)}");
                return true;
            }

            case "/debug":
            {
                var role = _permissionService.GetUserRole(evt.UserId, evt.GroupId);
                if (role != QQUserRole.Admin)
                {
                    await _client.SendMessageAsync(evt, "⚠️ 此命令仅管理员可用。");
                    return true;
                }

                var newState = DebugModeService.Toggle();
                var statusMsg = newState ? "✅ 调试模式已开启" : "✅ 调试模式已关闭";
                await _client.SendMessageAsync(evt, statusMsg);
                
                var userName = evt.Sender.DisplayName;
                var userId = evt.UserId;
                AnsiConsole.MarkupLine($"[grey][[QQ]][/] [yellow]Debug mode {(newState ? "enabled" : "disabled")}[/] by [green]{Markup.Escape(userName)}[/] (uid={userId})");
                return true;
            }

            case "/help":
            {
                var helpText = "可用命令：\n"
                    + "/new 或 /clear - 清除当前会话\n"
                    + "/debug - 切换调试模式（仅管理员）\n"
                    + "/heartbeat trigger - 立即触发心跳检查\n"
                    + "/cron list - 查看定时任务列表\n"
                    + "/cron remove <id> - 删除定时任务\n"
                    + "/help - 显示此帮助信息\n\n"
                    + "直接输入问题即可与 DotBot 对话。";
                await _client.SendMessageAsync(evt, helpText);
                return true;
            }
        }

        if (cmd.StartsWith("/heartbeat"))
        {
            await HandleHeartbeatAsync(evt, cmd);
            return true;
        }

        if (cmd.StartsWith("/cron"))
        {
            await HandleCronAsync(evt, cmd);
            return true;
        }

        if (cmd.StartsWith("/"))
        {
            var msg = CommandHelper.FormatUnknownCommandMessage(text, KnownCommands);
            await _client.SendMessageAsync(evt, msg);
            return true;
        }

        return false;
    }

    private static readonly string[] KnownCommands =
    [
        "/new", "/clear", "/debug", "/help", "/heartbeat", "/cron"
    ];

    private async Task HandleHeartbeatAsync(OneBotMessageEvent evt, string cmd)
    {
        if (_heartbeatService == null)
        {
            await _client.SendMessageAsync(evt, "心跳服务未启用。");
            return;
        }

        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 1 ? parts[1] : "trigger";

        if (subCmd == "trigger")
        {
            await _client.SendMessageAsync(evt, "正在触发心跳检查...");
            var result = await _heartbeatService.TriggerNowAsync();
            if (result != null)
                await _client.SendMessageAsync(evt, $"心跳结果：\n{result}");
            else
                await _client.SendMessageAsync(evt, "无心跳响应（HEARTBEAT.md 可能不存在或为空）。");
        }
        else
        {
            await _client.SendMessageAsync(evt, "用法：/heartbeat trigger");
        }
    }

    private async Task HandleCronAsync(OneBotMessageEvent evt, string cmd)
    {
        if (_cronService == null)
        {
            await _client.SendMessageAsync(evt, "定时任务服务未启用。");
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
                    await _client.SendMessageAsync(evt, "暂无定时任务。");
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"定时任务 ({jobs.Count})：");
                foreach (var job in jobs)
                {
                    var status = job.Enabled ? "已启用" : "已禁用";
                    var schedDesc = job.Schedule.Kind switch
                    {
                        "at" when job.Schedule.AtMs.HasValue =>
                            $"once at {DateTimeOffset.FromUnixTimeMilliseconds(job.Schedule.AtMs.Value):u}",
                        "every" when job.Schedule.EveryMs.HasValue =>
                            $"every {TimeSpan.FromMilliseconds(job.Schedule.EveryMs.Value)}",
                        _ => job.Schedule.Kind
                    };
                    var next = job.State.NextRunAtMs.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(job.State.NextRunAtMs.Value).ToString("u")
                        : "-";
                    sb.AppendLine($"[{job.Id}] {job.Name} ({status})");
                    sb.AppendLine($"  计划：{schedDesc}");
                    sb.AppendLine($"  下次执行：{next}");
                }
                await _client.SendMessageAsync(evt, sb.ToString().TrimEnd());
                break;
            }
            case "remove":
            {
                if (parts.Length < 3)
                {
                    await _client.SendMessageAsync(evt, "用法：/cron remove <任务ID>");
                    break;
                }
                var jobId = parts[2];
                if (_cronService.RemoveJob(jobId))
                    await _client.SendMessageAsync(evt, $"任务 '{jobId}' 已删除。");
                else
                    await _client.SendMessageAsync(evt, $"未找到任务 '{jobId}'。");
                break;
            }
            default:
                await _client.SendMessageAsync(evt, "用法：/cron list | /cron remove <任务ID>");
                break;
        }
    }

    private static void LogIncoming(string type, string targetId, string sender, string text)
    {
        var tag = type == "group"
            ? $"[cyan]Group {Markup.Escape(targetId)}[/]"
            : "[cyan]Private[/]";
        var preview = text.Length > 80 ? text[..80] + "..." : text;
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] {tag} [green]{Markup.Escape(sender)}[/]: {Markup.Escape(preview)}");
    }

    private static void LogOutgoing(OneBotMessageEvent evt, string text)
    {
        var tag = evt.IsGroupMessage
            ? $"[cyan]Group {evt.GroupId}[/]"
            : "[cyan]Private[/]";
        var preview = text.Length > 80 ? text[..80] + "..." : text;
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] {tag} [blue]DotBot[/]: {Markup.Escape(preview)}");
    }

    private static void LogError(string sender, string error)
    {
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] [red]Error[/] processing message from [green]{Markup.Escape(sender)}[/]: {Markup.Escape(error)}");
    }

    private static void LogUnauthorized(string type, string targetId, string sender)
    {
        var tag = type == "group"
            ? $"[cyan]Group {Markup.Escape(targetId)}[/]"
            : "[cyan]Private[/]";
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] {tag} [yellow]Unauthorized[/] user [green]{Markup.Escape(sender)}[/] ignored");
    }

    private static void LogToolCall(string name, IDictionary<string, object?>? args)
    {
        var icon = ToolIconRegistry.GetToolIcon(name);
        var argsStr = string.Empty;
        if (args != null)
        {
            try { argsStr = JsonSerializer.Serialize(args, SerializerOptions); }
            catch { argsStr = args.ToString() ?? string.Empty; }
        }
        
        var displayArgs = DebugModeService.IsEnabled() 
            ? argsStr 
            : (argsStr.Length > 150 ? argsStr[..150] + "..." : argsStr);
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] [yellow]{Markup.Escape($"{icon} {name}")}[/] [dim]{Markup.Escape(displayArgs)}[/]");
    }

    private static void LogToolResult(string? result)
    {
        var text = result ?? "(no output)";
        var preview = text.Length > 200 ? text[..200] + "..." : text;
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] [grey]Result: {Markup.Escape(preview)}[/]");
    }
}

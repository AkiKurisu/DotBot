using System.Text;
using System.Text.Json;
using DotBot.Agents;
using DotBot.CLI;
using DotBot.Commands.ChannelAdapters;
using DotBot.Commands.Core;
using DotBot.Context;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Gateway;
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

    private readonly SessionGate _sessionGate;

    private readonly TraceCollector? _traceCollector;

    private readonly TokenUsageStore? _tokenUsageStore;
    
    private readonly AgentFactory? _agentFactory;
    
    private readonly CommandDispatcher _commandDispatcher;

    public WeComChannelAdapter(
        AIAgent agent,
        SessionStore sessionStore,
        WeComBotRegistry registry,
        WeComPermissionService permissionService,
        WeComApprovalService approvalService,
        SessionGate sessionGate,
        HeartbeatService? heartbeatService = null,
        CronService? cronService = null,
        AgentFactory? agentFactory = null,
        TraceCollector? traceCollector = null,
        TokenUsageStore? tokenUsageStore = null)
    {
        _agent = agent;
        _sessionStore = sessionStore;
        _heartbeatService = heartbeatService;
        _cronService = cronService;
        _agentFactory = agentFactory;
        _permissionService = permissionService;
        _approvalService = approvalService;
        _sessionGate = sessionGate;
        _traceCollector = traceCollector;
        _tokenUsageStore = tokenUsageStore;
        
        // Initialize command dispatcher with built-in handlers
        _commandDispatcher = CommandDispatcher.CreateDefault();

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
            using var ____ = await _sessionGate.AcquireAsync(sessionId);

            var session = await _sessionStore.LoadOrCreateAsync(_agent, sessionId, CancellationToken.None);

            var textBuffer = new StringBuilder();
            long inputTokens = 0, outputTokens = 0, totalTokens = 0;
            var tokenTracker = _agentFactory?.GetOrCreateTokenTracker(sessionId);

            _traceCollector?.RecordSessionMetadata(
                sessionId,
                null,
                _agentFactory?.LastCreatedTools?.Select(t => t.Name));

            TracingChatClient.CurrentSessionKey = sessionId;
            TracingChatClient.ResetCallState(sessionId);
            try
            {
                await foreach (var update in _agent.RunStreamingAsync(RuntimeContextBuilder.AppendTo(plainText), session))
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
                                break;

                            case FunctionResultContent fr:
                                LogToolResult(Agents.ImageContentSanitizingChatClient.DescribeResult(fr.Result));
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

            if (totalTokens > 0)
            {
                tokenTracker?.Update(inputTokens, outputTokens);
                var displayInput = tokenTracker?.LastInputTokens ?? inputTokens;
                var displayOutput = tokenTracker?.TotalOutputTokens ?? outputTokens;
                textBuffer.Append($"\n\n[↑ {displayInput} input ↓ {displayOutput} output]");
            }

            await FlushTextBufferAsync(pusher, textBuffer);

            if (totalTokens > 0)
            {
                _tokenUsageStore?.Record(new TokenUsageRecord
                {
                    Source = TokenUsageSource.WeCom,
                    UserId = from.UserId,
                    DisplayName = from.Name,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens
                });
            }

            _agentFactory?.TryConsolidateMemory(session, sessionId);

            await _sessionStore.SaveAsync(_agent, session, sessionId, CancellationToken.None);
        }
        catch (SessionGateOverflowException)
        {
            AnsiConsole.MarkupLine(
                $"[grey][[WeCom]][/] [yellow]Request evicted for session {Markup.Escape(sessionId)} (queue overflow)[/]");
            try
            {
                await pusher.PushTextAsync("消息过多，该条已跳过，请稍后重试。");
            }
            catch
            {
                // ignored
            }
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
        // Get user info from context
        var chatContext = WeComChatContextScope.Current;
        var userId = chatContext?.UserId ?? "";
        var userName = chatContext?.UserName ?? "";
        var chatId = pusher.GetChatId();
        
        var userRole = _permissionService.GetUserRole(userId, chatId);
        var context = new CommandContext
        {
            SessionId = sessionId,
            RawText = text,
            UserId = userId,
            UserName = userName,
            IsAdmin = userRole == WeComUserRole.Admin,
            Source = "WeCom",
            GroupId = chatId,
            SessionStore = _sessionStore,
            HeartbeatService = _heartbeatService,
            CronService = _cronService,
            AgentFactory = _agentFactory
        };
        
        var responder = new WeComCommandResponder(pusher);
        return await _commandDispatcher.TryDispatchAsync(text, context, responder);
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

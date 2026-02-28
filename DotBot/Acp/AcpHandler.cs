using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotBot.Agents;
using DotBot.Commands.Custom;
using DotBot.Context;
using DotBot.DashBoard;
using DotBot.Memory;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotBot.Acp;

/// <summary>
/// Core handler for all ACP protocol messages.
/// Manages initialization, sessions, prompt turns, and tool call reporting.
/// </summary>
public sealed class AcpHandler(
    AcpTransport transport,
    SessionStore sessionStore,
    AgentFactory agentFactory,
    AIAgent agent,
    AcpApprovalService approvalService,
    string workspacePath,
    CustomCommandLoader? customCommandLoader = null,
    TraceCollector? traceCollector = null,
    AcpLogger? logger = null)
{
    private ClientCapabilities? _clientCapabilities;
    private AcpClientProxy? _clientProxy;
    private bool _initialized;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public const int ProtocolVersion = 1;

    /// <summary>
    /// Main message processing loop. Reads requests from transport and dispatches them.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var request = await transport.ReadRequestAsync(ct);
            if (request == null) break; // EOF

            try
            {
                await DispatchAsync(request, ct);
            }
            catch (Exception ex)
            {
                logger?.LogError($"Error handling {request.Method}", ex);
                AnsiConsole.MarkupLine($"[red][[ACP]] Error handling {Markup.Escape(request.Method)}: {Markup.Escape(ex.Message)}[/]");
                if (!request.IsNotification)
                {
                    transport.SendError(request.Id, -32603, ex.Message);
                }
            }
        }
    }

    private async Task DispatchAsync(JsonRpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case AcpMethods.Initialize:
                HandleInitialize(request);
                break;

            case AcpMethods.SessionNew:
                HandleSessionNew(request);
                break;

            case AcpMethods.SessionLoad:
                await HandleSessionLoadAsync(request, ct);
                break;

            case AcpMethods.SessionList:
                HandleSessionList(request);
                break;

            case AcpMethods.SessionPrompt:
                // Run prompt asynchronously so we can handle cancellation.
                // Exceptions must be caught here; the discarded Task would swallow them.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleSessionPromptAsync(request, ct);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError($"Unhandled prompt exception", ex);
                        transport.SendError(request.Id, -32603, ex.Message);
                    }
                }, ct);
                break;

            case AcpMethods.SessionCancel:
                HandleSessionCancel(request);
                break;

            default:
                if (!request.IsNotification)
                {
                    transport.SendError(request.Id, -32601, $"Method not found: {request.Method}");
                }
                break;
        }
    }

    private void HandleInitialize(JsonRpcRequest request)
    {
        var p = Deserialize<InitializeParams>(request.Params);

        _clientCapabilities = p?.ClientCapabilities;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        var result = new InitializeResult
        {
            ProtocolVersion = ProtocolVersion,
            AgentCapabilities = new AgentCapabilities
            {
                LoadSession = true,
                ListSessions = true,
                PromptCapabilities = new PromptCapabilities
                {
                    Text = true,
                    EmbeddedContext = true
                }
            },
            AgentInfo = new AgentInfo
            {
                Name = "DotBot",
                Title = "DotBot AI Agent",
                Version = version
            }
        };

        _initialized = true;
        _clientProxy = new AcpClientProxy(transport, _clientCapabilities);
        transport.SendResponse(request.Id, result);

        var caps = new List<string>();
        if (_clientProxy.SupportsFileRead) caps.Add("fs.read");
        if (_clientProxy.SupportsFileWrite) caps.Add("fs.write");
        if (_clientProxy.SupportsTerminal) caps.Add("terminal");
        var capsStr = caps.Count > 0 ? string.Join(", ", caps) : "none";
        logger?.LogEvent($"Initialized (client capabilities: {capsStr})");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Initialized (client capabilities: {Markup.Escape(capsStr)})");
    }

    private void HandleSessionNew(JsonRpcRequest request)
    {
        if (!EnsureInitialized(request)) return;

        var p = Deserialize<SessionNewParams>(request.Params);
        var sessionId = $"acp:{SessionStore.GenerateSessionId()}";

        approvalService.SetSessionId(sessionId);

        var result = new SessionNewResult
        {
            SessionId = sessionId,
            ConfigOptions = BuildConfigOptions()
        };

        transport.SendResponse(request.Id, result);

        // Send slash commands after session creation
        BroadcastSlashCommands(sessionId);

        logger?.LogEvent($"Session created: {sessionId}");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Session created: {Markup.Escape(sessionId)}");
    }

    private async Task HandleSessionLoadAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!EnsureInitialized(request)) return;

        var p = Deserialize<SessionLoadParams>(request.Params);
        if (p == null)
        {
            transport.SendError(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;
        approvalService.SetSessionId(sessionId);

        // Load the session to get chat history
        var session = await sessionStore.LoadOrCreateAsync(agent, sessionId, ct);
        var chatHistory = session.GetService<ChatHistoryProvider>();

        if (chatHistory is InMemoryChatHistoryProvider memoryProvider)
        {
            // Replay conversation as session/update notifications
            foreach (var msg in memoryProvider)
            {
                if (msg.Role == ChatRole.User)
                {
                    transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
                    {
                        SessionId = sessionId,
                        Update = new AcpSessionUpdate
                        {
                            SessionUpdate = AcpUpdateKind.UserMessageChunk,
                            Content = new AcpContentBlock { Type = "text", Text = msg.Text }
                        }
                    });
                }
                else if (msg.Role == ChatRole.Assistant && !string.IsNullOrEmpty(msg.Text))
                {
                    transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
                    {
                        SessionId = sessionId,
                        Update = new AcpSessionUpdate
                        {
                            SessionUpdate = AcpUpdateKind.AgentMessageChunk,
                            Content = new AcpContentBlock { Type = "text", Text = msg.Text }
                        }
                    });
                }
            }
        }

        transport.SendResponse(request.Id, new SessionLoadResult { SessionId = sessionId });
        BroadcastSlashCommands(sessionId);
        logger?.LogEvent($"Session loaded: {sessionId}");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Session loaded: {Markup.Escape(sessionId)}");
    }

    private void HandleSessionList(JsonRpcRequest request)
    {
        if (!EnsureInitialized(request)) return;

        var sessions = sessionStore.ListSessions()
            .Where(s => s.Key.StartsWith("acp:"))
            .Select(s => new SessionListEntry
            {
                SessionId = s.Key,
                UpdatedAt = s.UpdatedAt,
                Cwd = workspacePath
            })
            .ToList();

        transport.SendResponse(request.Id, new SessionListResult { Sessions = sessions });
    }

    private async Task HandleSessionPromptAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!EnsureInitialized(request)) return;

        var p = Deserialize<SessionPromptParams>(request.Params);
        if (p == null)
        {
            transport.SendError(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;
        approvalService.SetSessionId(sessionId);

        using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activePrompts[sessionId] = promptCts;

        try
        {
            // Extract prompt text from content blocks
            var promptText = ExtractPromptText(p.Prompt);

            // Handle slash commands
            if (p.Command != null)
            {
                promptText = $"/{p.Command} {promptText}".Trim();
            }

            if (customCommandLoader != null && promptText.StartsWith('/'))
            {
                var resolved = customCommandLoader.TryResolve(promptText);
                if (resolved != null)
                    promptText = resolved.ExpandedPrompt;
            }

            promptText = RuntimeContextBuilder.AppendTo(promptText);

            logger?.LogEvent($"Prompt start [session={sessionId}]: {(promptText.Length > 200 ? promptText[..200] + "..." : promptText)}");

            // Set up tracing
            TracingChatClient.CurrentSessionKey = sessionId;
            TracingChatClient.ResetCallState(sessionId);

            traceCollector?.RecordSessionMetadata(
                sessionId, null,
                agentFactory.LastCreatedTools?.Select(t => t.Name));

            var session = await sessionStore.LoadOrCreateAsync(agent, sessionId, promptCts.Token);
            var sb = new StringBuilder();
            long inputTokens = 0, outputTokens = 0, totalTokens = 0;
            var tokenTracker = agentFactory.GetOrCreateTokenTracker(sessionId);

            try
            {
                await foreach (var update in agent.RunStreamingAsync(promptText, session, cancellationToken: promptCts.Token))
                {
                    foreach (var content in update.Contents)
                    {
                        switch (content)
                        {
                            case FunctionCallContent fc:
                                SendToolCallStarted(sessionId, fc);
                                break;

                            case FunctionResultContent fr:
                                SendToolCallCompleted(sessionId, fr);
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
                    {
                        sb.Append(update.Text);
                        SendMessageChunk(sessionId, update.Text);
                    }
                }
            }
            finally
            {
                TracingChatClient.ResetCallState(sessionId);
                TracingChatClient.CurrentSessionKey = null;
            }

            if (totalTokens == 0 && (inputTokens > 0 || outputTokens > 0))
                totalTokens = inputTokens + outputTokens;

            await sessionStore.SaveAsync(agent, session, sessionId, CancellationToken.None);

            if (totalTokens > 0)
                tokenTracker.Update(inputTokens, outputTokens);

            // Handle context compaction
            if (agentFactory is { Compactor: not null, MaxContextTokens: > 0 } &&
                inputTokens >= agentFactory.MaxContextTokens)
            {
                if (await agentFactory.Compactor.TryCompactAsync(session, promptCts.Token))
                {
                    tokenTracker.Reset();
                    traceCollector?.RecordContextCompaction(sessionId);
                }
            }

            agentFactory.TryConsolidateMemory(session, sessionId);

            logger?.LogEvent($"Prompt complete [session={sessionId}] stop=end_turn tokens(in={inputTokens},out={outputTokens},total={totalTokens})");

            transport.SendResponse(request.Id, new SessionPromptResult
            {
                StopReason = AcpStopReason.EndTurn
            });
        }
        catch (OperationCanceledException)
        {
            logger?.LogEvent($"Prompt cancelled [session={sessionId}]");
            transport.SendResponse(request.Id, new SessionPromptResult
            {
                StopReason = AcpStopReason.Cancelled
            });
        }
        catch (Exception ex)
        {
            logger?.LogError($"Prompt error [session={sessionId}]", ex);
            AnsiConsole.MarkupLine($"[red][[ACP]][/] Prompt error: {Markup.Escape(ex.Message)}");
            transport.SendError(request.Id, -32603, ex.Message);
        }
        finally
        {
            _activePrompts.TryRemove(sessionId, out _);
        }
    }

    private void HandleSessionCancel(JsonRpcRequest request)
    {
        var p = Deserialize<SessionCancelParams>(request.Params);
        if (p == null) return;

        if (_activePrompts.TryGetValue(p.SessionId, out var cts))
        {
            cts.Cancel();
            logger?.LogEvent($"Session cancel requested: {p.SessionId}");
            AnsiConsole.MarkupLine($"[yellow][[ACP]][/] Cancelled session: {Markup.Escape(p.SessionId)}");
        }
    }

    // ───── Helper methods ─────

    private void SendMessageChunk(string sessionId, string text)
    {
        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.AgentMessageChunk,
                Content = new AcpContentBlock { Type = "text", Text = text }
            }
        });
    }

    private void SendToolCallStarted(string sessionId, FunctionCallContent fc)
    {
        var kind = AcpToolKindMapper.GetKind(fc.Name);
        var filePaths = AcpToolKindMapper.ExtractFilePaths(fc.Name, fc.Arguments);

        var argsStr = string.Empty;
        if (fc.Arguments != null)
        {
            try
            {
                argsStr = JsonSerializer.Serialize(fc.Arguments, JsonOptions);
            }
            catch
            {
                argsStr = fc.Arguments.ToString() ?? "";
            }
        }

        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.ToolCall,
                ToolCallId = fc.CallId,
                Title = fc.Name,
                Kind = kind,
                Status = AcpToolStatus.InProgress,
                Content = string.IsNullOrEmpty(argsStr)
                    ? null
                    : new List<AcpContentBlock> { new() { Type = "text", Text = argsStr } },
                FileLocations = filePaths?.Select(p => new AcpFileLocation { Uri = $"file://{p}" }).ToList()
            }
        });
    }

    private void SendToolCallCompleted(string sessionId, FunctionResultContent fr)
    {
        var resultText = ImageContentSanitizingChatClient.DescribeResult(fr.Result);
        var preview = resultText.Length > 500 ? resultText[..500] + "..." : resultText;

        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.ToolCallUpdate,
                ToolCallId = fr.CallId,
                Status = fr.Result is Exception ? AcpToolStatus.Failed : AcpToolStatus.Completed,
                Content = new List<AcpContentBlock> { new() { Type = "text", Text = preview } }
            }
        });
    }

    private void BroadcastSlashCommands(string sessionId)
    {
        if (customCommandLoader == null) return;

        var commands = customCommandLoader.ListCommands()
            .Select(c => new AcpSlashCommand
            {
                Name = c.Name,
                Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description
            })
            .ToList();

        if (commands.Count == 0) return;

        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.AvailableCommandsUpdate,
                Commands = commands
            }
        });
    }

    private static List<ConfigOption> BuildConfigOptions()
    {
        return
        [
            new ConfigOption
            {
                Id = "mode",
                Name = "Mode",
                Category = "mode",
                CurrentValue = "agent",
                Options =
                [
                    new ConfigOptionValue { Value = "agent", Name = "Agent", Description = "Full agent mode with all tools" }
                ]
            }
        ];
    }

    private static string ExtractPromptText(List<AcpContentBlock> prompt)
    {
        if (prompt.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var block in prompt)
        {
            switch (block.Type)
            {
                case "text" when !string.IsNullOrEmpty(block.Text):
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(block.Text);
                    break;

                case "resource" when block.Resource != null:
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"[File: {block.Resource.Uri}]");
                    if (!string.IsNullOrEmpty(block.Resource.Text))
                    {
                        sb.Append('\n');
                        sb.Append(block.Resource.Text);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    private bool EnsureInitialized(JsonRpcRequest request)
    {
        if (_initialized) return true;
        transport.SendError(request.Id, -32002, "Agent not initialized. Call 'initialize' first.");
        return false;
    }

    private static T? Deserialize<T>(JsonElement? element) where T : class
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Undefined)
            return null;
        return element.Value.Deserialize<T>(JsonOptions);
    }
}

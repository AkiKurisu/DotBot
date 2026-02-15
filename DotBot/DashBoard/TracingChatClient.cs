using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace DotBot.DashBoard;

/// <summary>
/// DelegatingChatClient that records trace events for Dashboard.
/// Designed to be placed INSIDE FunctionInvokingChatClient so it intercepts
/// each individual LLM call (including follow-up calls after tool execution).
/// Tool calls are detected from LLM responses; tool results are detected
/// from input messages on follow-up calls by FunctionInvokingChatClient.
///
/// State is stored per session key in a ConcurrentDictionary instead of AsyncLocal,
/// because FunctionInvokingChatClient calls this client's streaming method multiple
/// times across async enumerable boundaries where AsyncLocal copy-on-write semantics
/// prevent state from being shared between invocations.
/// </summary>
public sealed class TracingChatClient(IChatClient innerClient, TraceCollector collector) : DelegatingChatClient(innerClient)
{
    private static readonly AsyncLocal<string?> SessionKeyLocal = new();

    /// <summary>
    /// Per-session shared state that survives across multiple calls from FunctionInvokingChatClient.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SessionCallState> SessionStates = new();

    public static string? CurrentSessionKey
    {
        get => SessionKeyLocal.Value;
        set => SessionKeyLocal.Value = value;
    }

    public static void ResetCallState(string? sessionKey = null)
    {
        var key = sessionKey ?? CurrentSessionKey;
        if (key != null)
            SessionStates.TryRemove(key, out _);
    }

    private static SessionCallState GetOrCreateState(string sessionKey)
    {
        return SessionStates.GetOrAdd(sessionKey, _ => new SessionCallState());
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sessionKey = CurrentSessionKey!;
        var messages = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();
        var state = GetOrCreateState(sessionKey);

        // Record request only on first call
        RecordRequestIfFirst(sessionKey, messages, state);

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            collector.RecordError(sessionKey, ex.Message);
            throw;
        }

        RecordToolCallsFromResponse(sessionKey, response.Messages, state);

        // Record response if we have any text (regardless of tool calls)
        // Tool calls may happen in earlier iterations, but the final iteration will have the actual response
        var responseText = response.Text;
        if (!string.IsNullOrEmpty(responseText))
        {
            collector.RecordResponse(
                sessionKey,
                responseText,
                GetStringProperty(response, "ResponseId"),
                GetStringProperty(response, "MessageId"),
                GetStringProperty(response, "ModelId"),
                GetStringProperty(response, "FinishReason"),
                GetProperty(response, "AdditionalProperties"));
        }

        if (response.Usage != null)
        {
            var input = response.Usage.InputTokenCount ?? 0;
            var output = response.Usage.OutputTokenCount ?? 0;
            if (input > 0 || output > 0)
                collector.RecordTokenUsage(sessionKey, input, output);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionKey = CurrentSessionKey!;
        var messages = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();
        var state = GetOrCreateState(sessionKey);

        // Record request only on first call
        RecordRequestIfFirst(sessionKey, messages, state);

        var responseBuffer = new StringBuilder();
        long inputTokens = 0, outputTokens = 0;

        IAsyncEnumerable<ChatResponseUpdate> stream;
        try
        {
            stream = base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            collector.RecordError(sessionKey, ex.Message);
            throw;
        }

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            state.LastUpdate = update;

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent fc:
                    {
                        var callId = fc.CallId ?? "";
                        if (state.ProcessedCallIds.Add($"call:{callId}"))
                        {
                            collector.RecordToolCallStarted(sessionKey, fc);
                            if (!string.IsNullOrEmpty(callId))
                            {
                                state.ToolTimers[callId] = Stopwatch.StartNew();
                                state.ToolNameMap[callId] = fc.Name;
                            }
                        }
                        break;
                    }
                    case FunctionResultContent fr:
                    {
                        var resultCallId = fr.CallId;
                        if (state.ProcessedCallIds.Add($"result:{resultCallId}"))
                        {
                            if (state.ToolTimers.TryGetValue(resultCallId, out var timer))
                            {
                                timer.Stop();
                                var toolName = state.ToolNameMap.GetValueOrDefault(resultCallId, "unknown");
                                collector.RecordToolCallCompleted(sessionKey, fr, toolName, timer.ElapsedMilliseconds);
                                state.ToolTimers.Remove(resultCallId);
                                state.ToolNameMap.Remove(resultCallId);
                            }
                        }
                        break;
                    }
                    case UsageContent usage:
                    {
                        if (usage.Details.InputTokenCount.HasValue)
                            inputTokens = usage.Details.InputTokenCount.Value;
                        if (usage.Details.OutputTokenCount.HasValue)
                            outputTokens = usage.Details.OutputTokenCount.Value;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(update.Text))
                responseBuffer.Append(update.Text);

            yield return update;
        }

        if (responseBuffer.Length > 0)
        {
            var lastUpdate = state.LastUpdate;
            collector.RecordResponse(
                sessionKey,
                responseBuffer.ToString(),
                GetStringProperty(lastUpdate, "ResponseId"),
                GetStringProperty(lastUpdate, "MessageId"),
                GetStringProperty(lastUpdate, "ModelId"),
                GetStringProperty(lastUpdate, "FinishReason"),
                GetProperty(lastUpdate, "AdditionalProperties"));
        }

        if (inputTokens > 0 || outputTokens > 0)
            collector.RecordTokenUsage(sessionKey, inputTokens, outputTokens);
    }

    private static object? GetProperty(object? instance, string propertyName)
    {
        if (instance == null)
            return null;

        var property = instance
            .GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        return property?.GetValue(instance);
    }

    private static string? GetStringProperty(object? instance, string propertyName)
    {
        var value = GetProperty(instance, propertyName);
        return value?.ToString();
    }

    private void RecordRequestIfFirst(string sessionKey, IList<ChatMessage> messages, SessionCallState state)
    {
        if (state.RequestRecorded)
            return;

        // Find the last user message anywhere in the message list
        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUserMsg != null)
        {
            var text = lastUserMsg.Text;
            if (!string.IsNullOrEmpty(text))
            {
                state.RequestRecorded = true;
                collector.RecordRequest(sessionKey, text);
            }
        }
    }

    private void RecordToolCallsFromResponse(
        string sessionKey,
        IList<ChatMessage> responseMessages,
        SessionCallState state)
    {
        foreach (var msg in responseMessages)
        {
            // Record FunctionCallContent
            foreach (var fc in msg.Contents.OfType<FunctionCallContent>())
            {
                var callId = fc.CallId;
                if (!state.ProcessedCallIds.Add($"call:{callId}")) continue;

                collector.RecordToolCallStarted(sessionKey, fc);
                if (!string.IsNullOrEmpty(callId))
                {
                    state.ToolTimers[callId] = Stopwatch.StartNew();
                    state.ToolNameMap[callId] = fc.Name;
                }
            }

            // Record FunctionResultContent
            foreach (var fr in msg.Contents.OfType<FunctionResultContent>())
            {
                var callId = fr.CallId;
                if (!state.ProcessedCallIds.Add($"result:{callId}")) continue;

                if (state.ToolTimers.TryGetValue(callId, out var timer))
                {
                    timer.Stop();
                    var toolName = state.ToolNameMap.GetValueOrDefault(callId, "unknown");
                    collector.RecordToolCallCompleted(sessionKey, fr, toolName, timer.ElapsedMilliseconds);
                    state.ToolTimers.Remove(callId);
                    state.ToolNameMap.Remove(callId);
                }
            }
        }
    }

    /// <summary>
    /// Holds per-session state shared across multiple calls from FunctionInvokingChatClient.
    /// </summary>
    private sealed class SessionCallState
    {
        public bool RequestRecorded;
        public readonly HashSet<string> ProcessedCallIds = new();
        public readonly Dictionary<string, Stopwatch> ToolTimers = new();
        public readonly Dictionary<string, string> ToolNameMap = new();
        public ChatResponseUpdate? LastUpdate;
    }
}

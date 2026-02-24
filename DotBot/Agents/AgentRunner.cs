using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotBot.CLI;
using DotBot.DashBoard;
using DotBot.Gateway;
using DotBot.Memory;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotBot.Agents;

/// <summary>
/// Shared agent execution logic used across all channel modes (QQ, WeCom, CLI).
/// Eliminates duplicated RunAgent local functions in Program.cs.
/// </summary>
public sealed class AgentRunner(AIAgent agent, SessionStore sessionStore, AgentFactory? agentFactory = null, TraceCollector? traceCollector = null, SessionGate? sessionGate = null)
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
    /// <summary>
    /// Run agent with a prompt, manage session lifecycle, stream output, and log results.
    /// </summary>
    public async Task<string?> RunAsync(string prompt, string sessionKey)
    {
        var tag = sessionKey.StartsWith("heartbeat") ? "Heartbeat"
            : sessionKey.StartsWith("cron:") ? "Cron"
            : "Agent";

        if (sessionKey.StartsWith("cron:"))
        {
            prompt = $"[System: Scheduled Task Triggered]\n" +
                     $"The following is a scheduled cron job that has just been triggered. " +
                     $"Execute the task described below directly and respond with the result. " +
                     $"Do NOT treat this as a user conversation or create a new scheduled task.\n\n" +
                     $"Task: {prompt}";
        }

        AnsiConsole.MarkupLine(
            $"[grey][[{tag}]][/] Running: [dim]{Markup.Escape(prompt.Length > 120 ? prompt[..120] + "..." : prompt)}[/]");

        IDisposable? gateLock = null;
        try
        {
            if (sessionGate != null)
                gateLock = await sessionGate.AcquireAsync(sessionKey);
        }
        catch (SessionGateOverflowException)
        {
            AnsiConsole.MarkupLine(
                $"[grey][[{tag}]][/] [yellow]Request evicted for session {Markup.Escape(sessionKey)} (queue overflow)[/]");
            return null;
        }

        try
        {

        var session = await sessionStore.LoadOrCreateAsync(agent, sessionKey, CancellationToken.None);
        var sb = new StringBuilder();
        long inputTokens = 0, outputTokens = 0, totalTokens = 0;
        var tokenTracker = agentFactory?.GetOrCreateTokenTracker(sessionKey);

        traceCollector?.RecordSessionMetadata(
            sessionKey,
            null,
            agentFactory?.LastCreatedTools?.Select(t => t.Name));
        traceCollector?.RecordRequest(sessionKey, prompt);

        var toolTimers = new Dictionary<string, Stopwatch>();
        var toolNameMap = new Dictionary<string, string>();

        TracingChatClient.CurrentSessionKey = sessionKey;
        TracingChatClient.ResetCallState(sessionKey);
        try
        {
            await foreach (var update in agent.RunStreamingAsync(prompt, session))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent fc:
                        {
                            var icon = ToolIconRegistry.GetToolIcon(fc.Name);
                            var argsStr = string.Empty;
                            if (fc.Arguments != null)
                            {
                                try
                                {
                                    argsStr = JsonSerializer.Serialize(fc.Arguments, _jsonSerializerOptions);
                                }
                                catch
                                {
                                    argsStr = fc.Arguments.ToString() ?? string.Empty;
                                }
                            }

                            var preview = argsStr.Length > 150 ? argsStr[..150] + "..." : argsStr;
                            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [yellow]{Markup.Escape($"{icon} {fc.Name}")}[/] [dim]{Markup.Escape(preview)}[/]");

                            traceCollector?.RecordToolCallStarted(sessionKey, fc);
                            if (!string.IsNullOrEmpty(fc.CallId))
                            {
                                toolTimers[fc.CallId] = Stopwatch.StartNew();
                                toolNameMap[fc.CallId] = fc.Name;
                            }
                            break;
                        }
                        case FunctionResultContent fr:
                        {
                            var result = fr.Result?.ToString() ?? "(no output)";
                            var preview = result.Length > 200 ? result[..200] + "..." : result;
                            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [grey]Result: {Markup.Escape(preview)}[/]");

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
                            traceCollector?.RecordToolCallCompleted(sessionKey, fr, toolName, durationMs);
                            break;
                        }
                        case UsageContent usage:
                        {
                            if (usage.Details.InputTokenCount.HasValue)
                                inputTokens = usage.Details.InputTokenCount.Value;
                            if (usage.Details.OutputTokenCount.HasValue)
                                outputTokens = usage.Details.OutputTokenCount.Value;
                            if (usage.Details.TotalTokenCount.HasValue)
                                totalTokens = usage.Details.TotalTokenCount.Value;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(update.Text)) sb.Append(update.Text);
            }
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = null;
        }

        if (totalTokens == 0 && (inputTokens > 0 || outputTokens > 0))
            totalTokens = inputTokens + outputTokens;

        await sessionStore.SaveAsync(agent, session, sessionKey, CancellationToken.None);
        var response = sb.Length > 0 ? sb.ToString() : null;

        traceCollector?.RecordResponse(sessionKey, response);

        if (response != null)
        {
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] Response: [dim]{Markup.Escape(response.Length > 200 ? response[..200] + "..." : response)}[/]");
        }
        
        if (totalTokens > 0)
        {
            tokenTracker?.Update(inputTokens, outputTokens);
            var displayInput = tokenTracker?.LastInputTokens ?? inputTokens;
            var displayOutput = tokenTracker?.TotalOutputTokens ?? outputTokens;
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [blue]↑ {displayInput} input[/] [green]↓ {displayOutput} output[/]");
            traceCollector?.RecordTokenUsage(sessionKey, inputTokens, outputTokens);
        }

        if (agentFactory is { Compactor: not null, MaxContextTokens: > 0 } &&
            inputTokens >= agentFactory.MaxContextTokens)
        {
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [yellow]Context compacting...[/]");
            if (await agentFactory.Compactor.TryCompactAsync(session))
            {
                tokenTracker?.Reset();
                traceCollector?.RecordContextCompaction(sessionKey);
            }
        }

        agentFactory?.TryConsolidateMemory(session, sessionKey);

        return response;

        }
        finally
        {
            gateLock?.Dispose();
        }
    }
}

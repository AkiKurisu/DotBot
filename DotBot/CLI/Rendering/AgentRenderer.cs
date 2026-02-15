using System.Text;
using System.Threading.Channels;
using Spectre.Console;

namespace DotBot.CLI.Rendering;

/// <summary>
/// Core renderer for agent display.
/// Owns all Spectre.Console output and runs a single render loop.
///
/// Goal:
/// - When a tool starts: show a live <see cref="AnsiConsole.Status"/> spinner.
/// - When the tool completes: stop the spinner and emit a history.
/// - Stream model response chunks as normal text (kept as history).
/// - Handle approval prompts by pausing/resuming rendering.
/// </summary>
public sealed class AgentRenderer : IRenderControl, IDisposable
{
    private readonly Channel<RenderEvent> _eventQueue = Channel.CreateUnbounded<RenderEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    
    private Task? _renderTask;
    
    private CancellationTokenSource? _cancellationTokenSource;

    private RenderState _currentState = RenderState.Idle;

    private string? _currentToolIcon;
    
    private string? _currentToolTitle;
    
    private string? _currentToolContent;
    
    private string? _currentToolAdditional;

    private readonly StringBuilder _responseBuffer = new();
    private MarkdownConsoleRenderer.StreamSession? _markdownStreamSession;
    private bool _hasPendingUsage;
    private long _pendingInputTokens;
    private long _pendingOutputTokens;
    
    private bool _disposed;

    // For approval handling - action to execute on the render thread while paused
    private volatile Func<object?>? _pausedAction;
    private volatile TaskCompletionSource<object?>? _pausedActionResultTcs;

    private enum RenderState
    {
        Idle,
        ToolExecuting,
        Responding,
        ApprovalPaused
    }

    /// <summary>
    /// Start the rendering loop.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_renderTask != null)
        {
            throw new InvalidOperationException("Renderer already started");
        }

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _renderTask = Task.Run(() => RenderLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send a rendering event.
    /// </summary>
    public async ValueTask SendEventAsync(RenderEvent evt, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AgentRenderer));
        }

        await _eventQueue.Writer.WriteAsync(evt, cancellationToken);
    }

    /// <summary>
    /// Consume event stream and send to renderer.
    /// </summary>
    public async Task ConsumeEventsAsync(IAsyncEnumerable<RenderEvent> events, CancellationToken cancellationToken = default)
    {
        await foreach (var evt in events.WithCancellation(cancellationToken))
        {
            await SendEventAsync(evt, cancellationToken);
        }
    }

    /// <summary>
    /// Stop the rendering loop gracefully (drain queued events).
    /// </summary>
    public async Task StopAsync()
    {
        if (_renderTask == null)
        {
            return;
        }

        // Signal completion; do NOT cancel here, otherwise we may drop queued events.
        _eventQueue.Writer.TryComplete();

        try
        {
            await _renderTask;
        }
        catch (OperationCanceledException)
        {
            // Expected if outer cancellation token was canceled.
        }
    }

    /// <summary>
    /// Pause rendering, execute an action on the render thread, then resume.
    /// This ensures the action has exclusive console access without cross-thread
    /// live rendering conflicts with Spectre.Console (IRenderControl implementation).
    /// </summary>
    public async Task<T> ExecuteWhilePausedAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        var resultTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Store action and TCS BEFORE sending the event to avoid a race where
        // the render loop processes the event before fields are set.
        // The Channel write in SendEventAsync provides the memory barrier.
        _pausedAction = () => action();
        _pausedActionResultTcs = resultTcs;

        // Send approval required event to pause the Status spinner
        await SendEventAsync(RenderEvent.ApprovalRequest(), cancellationToken);

        // Wait for the render loop to execute the action and return the result
        var result = await resultTcs.Task;
        return (T)result!;
    }

    private async Task RenderLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reader = _eventQueue.Reader;

            while (await reader.WaitToReadAsync(cancellationToken))
            {
                while (reader.TryRead(out var evt))
                {
                    if (evt.Type == RenderEventType.ToolCallStarted)
                    {
                        await RunToolStatusSessionAsync(evt, cancellationToken);
                        continue;
                    }

                    ProcessNonToolStartEvent(evt);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Renderer error: {Markup.Escape(ex.Message)}[/]");
#if DEBUG
            AnsiConsole.WriteException(ex);
#endif
        }
    }

    /// <summary>
    /// Enters a live Status spinner session until a ToolCallCompleted arrives.
    /// The session keeps consuming the shared event queue, and buffers non-tool events
    /// to be rendered after the status is closed (to keep the console stable).
    /// </summary>
    private async Task RunToolStatusSessionAsync(RenderEvent toolStarted, CancellationToken cancellationToken)
    {
        CleanupCurrentState();

        _currentState = RenderState.ToolExecuting;
        _currentToolIcon = toolStarted.Icon;
        _currentToolTitle = toolStarted.Title;
        _currentToolContent = toolStarted.Content;
        _currentToolAdditional = toolStarted.AdditionalInfo;

        // Fallback: if neither tool name nor icon is present, use defaults
        // so the spinner still works for unregistered or parallel tool calls.
        if (string.IsNullOrWhiteSpace(_currentToolTitle) && string.IsNullOrWhiteSpace(_currentToolIcon))
        {
            _currentToolIcon = "🔧";
            _currentToolTitle = "Tool";
        }

        var reader = _eventQueue.Reader;
        RenderEvent? toolCompleted = null;
        var buffered = new List<RenderEvent>(capacity: 8);

        var initialStatus = BuildToolStatusMarkup();

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Color.Yellow))
                .StartAsync(initialStatus, async ctx =>
                {
                    ctx.Status = initialStatus;

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        // Wait for more events
                        if (!await reader.WaitToReadAsync(cancellationToken))
                        {
                            break;
                        }

                        while (reader.TryRead(out var evt))
                        {
                            switch (evt.Type)
                            {
                                case RenderEventType.ThinkingStep:
                                    // Treat thinking steps during tool execution as status updates
                                    _currentToolIcon = evt.Icon ?? _currentToolIcon;
                                    _currentToolTitle = evt.Title ?? _currentToolTitle;
                                    _currentToolContent = evt.Content;
                                    ctx.Status = BuildToolStatusMarkup();
                                    break;

                                case RenderEventType.ToolCallStarted:
                                    // Nested/second tool start: treat as a new status target
                                    _currentToolIcon = evt.Icon ?? _currentToolIcon;
                                    _currentToolTitle = evt.Title ?? _currentToolTitle;
                                    _currentToolContent = evt.Content;
                                    _currentToolAdditional = evt.AdditionalInfo ?? _currentToolAdditional;
                                    ctx.Status = BuildToolStatusMarkup();
                                    break;

                                case RenderEventType.ToolCallCompleted:
                                    toolCompleted = evt;
                                    return;

                                case RenderEventType.ApprovalRequired:
                                    // Pause for approval prompt.
                                    // The paused action and result TCS are already set by
                                    // ExecuteWhilePausedAsync before it sent this event,
                                    // and the Channel write provides the memory barrier.
                                    _currentState = RenderState.ApprovalPaused;
                                    
                                    // Exit Status context temporarily
                                    return;

                                default:
                                    buffered.Add(evt);
                                    break;
                            }
                        }
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // If canceled during a tool call, just exit.
        }

        // If we paused for approval, execute the paused action on the render thread
        if (_currentState == RenderState.ApprovalPaused)
        {
            // Execute the paused action on the render thread (same thread as Status)
            // to avoid cross-thread Spectre.Console live rendering issues
            var action = _pausedAction;
            var resultTcs = _pausedActionResultTcs;
            _pausedAction = null;
            _pausedActionResultTcs = null;

            if (action != null && resultTcs != null)
            {
                try
                {
                    var result = action();
                    resultTcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    resultTcs.TrySetException(ex);
                }
            }

            // Resume tool status session
            _currentState = RenderState.ToolExecuting;

            // Re-enter status spinner and wait for completion
            await RunToolStatusSessionAsync(
                RenderEvent.ToolStarted(_currentToolIcon, _currentToolTitle, _currentToolContent ?? string.Empty, _currentToolAdditional),
                cancellationToken);
            return;
        }

        // Emit the tool history line after Status closes.
        if (toolCompleted != null)
        {
            WriteToolHistoryLine(toolCompleted);
        }

        // Reset tool state
        _currentState = RenderState.Idle;
        _currentToolIcon = null;
        _currentToolTitle = null;
        _currentToolContent = null;
        _currentToolAdditional = null;

        // Replay buffered events
        foreach (var evt in buffered)
        {
            ProcessNonToolStartEvent(evt);
        }
    }

    private void ProcessNonToolStartEvent(RenderEvent evt)
    {
        switch (evt.Type)
        {
            case RenderEventType.ToolCallCompleted:
                // In case a completed event arrives without an active status session.
                WriteToolHistoryLine(evt);
                break;

            case RenderEventType.ResponseChunk:
                HandleResponseChunk(evt);
                break;

            case RenderEventType.ThinkingStep:
                HandleThinkingStep(evt);
                break;

            case RenderEventType.Warning:
                HandleWarning(evt);
                break;

            case RenderEventType.Error:
                HandleError(evt);
                break;

            case RenderEventType.Complete:
                HandleComplete(evt);
                break;

            case RenderEventType.Usage:
                HandleUsage(evt);
                break;

            case RenderEventType.ApprovalRequired:
            case RenderEventType.ApprovalCompleted:
                break;
        }
    }

    private string BuildToolStatusMarkup()
    {
        // If neither tool title nor icon is present, this is an invalid tool event.
        if (string.IsNullOrWhiteSpace(_currentToolTitle) && string.IsNullOrWhiteSpace(_currentToolIcon))
        {
            return "[red]Error: Invalid tool call (missing tool name/icon).[/]";
        }

        var iconPart = string.IsNullOrWhiteSpace(_currentToolIcon) ? string.Empty : _currentToolIcon;
        var titlePart = string.IsNullOrWhiteSpace(_currentToolTitle) ? string.Empty : _currentToolTitle;
        var header = (iconPart + " " + titlePart).Trim();

        var sb = new StringBuilder();
        sb.Append($"[yellow]{Markup.Escape(header)}[/]");
        var segmentMaxLength = GetStatusSegmentMaxLength();

        if (!string.IsNullOrWhiteSpace(_currentToolContent))
        {
            var displayContent = DebugModeService.IsEnabled() 
                ? _currentToolContent 
                : TruncateText(_currentToolContent!, segmentMaxLength);
            displayContent = NormalizeInlineText(displayContent);
            sb.Append($" [grey]{Markup.Escape(displayContent)}[/]");
        }

        if (!string.IsNullOrWhiteSpace(_currentToolAdditional))
        {
            var displayAdditional = DebugModeService.IsEnabled() 
                ? _currentToolAdditional 
                : TruncateText(_currentToolAdditional!, segmentMaxLength);
            displayAdditional = NormalizeInlineText(displayAdditional);
            sb.Append($" [dim]({Markup.Escape(displayAdditional)})[/]");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Convert a tool completion into a single-line history record.
    /// Uses the last known tool identity from the status session.
    /// </summary>
    private void WriteToolHistoryLine(RenderEvent evt)
    {
        var icon = evt.Icon ?? _currentToolIcon;
        var title = evt.Title ?? _currentToolTitle;

        // Fallback: if both are missing, use a default so we still show the result
        // instead of an error. This can happen when parallel tool calls complete
        // after the status session has already reset its state.
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(icon))
        {
            icon = "🔧";
            title = "Tool";
        }

        // "Content" in ToolCompleted contains the args (from StreamAdapter)
        // "AdditionalInfo" contains the result
        var args = evt.Content;
        var result = evt.AdditionalInfo;

        var line = new StringBuilder();
        line.Append($"[yellow]{Markup.Escape($"{icon} {title}")}[/]");

        // Display args in dim format (prefer event content over state)
        var argsToShow = !string.IsNullOrWhiteSpace(args) ? args : _currentToolAdditional;
        if (!string.IsNullOrWhiteSpace(argsToShow))
        {
            var displayArgs = DebugModeService.IsEnabled() 
                ? argsToShow 
                : TruncateText(argsToShow, 120);
            displayArgs = NormalizeInlineText(displayArgs);
            line.Append($" [dim]({Markup.Escape(displayArgs)})[/]");
        }

        AnsiConsole.MarkupLine(line.ToString());

        // Keep a separate line for result if it's not too long
        if (!string.IsNullOrWhiteSpace(result))
        {
            var truncatedResult = TruncateText(result, 200);
            AnsiConsole.MarkupLine($"[grey]Result: {Markup.Escape(truncatedResult)}[/]");
        }
    }

    private void HandleResponseChunk(RenderEvent evt)
    {
        // Use IsNullOrEmpty (not IsNullOrWhiteSpace) so that whitespace-only chunks
        // containing newlines are passed through to the markdown stream session.
        // These newlines are structurally significant for markdown block parsing.
        if (string.IsNullOrEmpty(evt.Content))
        {
            return;
        }

        if (_currentState != RenderState.Responding)
        {
            CleanupCurrentState();
            _currentState = RenderState.Responding;
            _responseBuffer.Clear();
            _hasPendingUsage = false;
            _markdownStreamSession = MarkdownConsoleRenderer.CreateStreamSession();
            AnsiConsole.WriteLine();
        }

        _responseBuffer.Append(evt.Content);
        _markdownStreamSession ??= MarkdownConsoleRenderer.CreateStreamSession();
        _markdownStreamSession.Append(evt.Content);
    }

    private void HandleThinkingStep(RenderEvent evt)
    {
        // If tool session is active, thinking steps will be consumed inside the status session.
        if (_currentState == RenderState.ToolExecuting)
        {
            return;
        }

        var icon = evt.Icon ?? "💭";
        var title = evt.Title ?? "Thinking";
        var color = evt.Color ?? "cyan";

        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape($"{icon} {title}")}[/] [dim]{Markup.Escape(evt.Content)}[/]");
    }

    private void HandleWarning(RenderEvent evt)
    {
        CleanupCurrentState();
        var color = evt.Color ?? "yellow";
        AnsiConsole.MarkupLine($"[{color}]⚠ {Markup.Escape(evt.Content)}[/]");
        _currentState = RenderState.Idle;
    }

    private void HandleError(RenderEvent evt)
    {
        CleanupCurrentState();
        var color = evt.Color ?? "red";
        AnsiConsole.MarkupLine($"[{color}]✗ {Markup.Escape(evt.Content)}[/]");
        _currentState = RenderState.Idle;
    }

    private void HandleUsage(RenderEvent evt)
    {
        var parts = evt.Content.Split(',');
        if (!(parts.Length >= 2 &&
              long.TryParse(parts[0], out var input) &&
              long.TryParse(parts[1], out var output)))
        {
            return;
        }

        if (_currentState == RenderState.Responding)
        {
            // Delay usage output until markdown stream is fully flushed.
            _pendingInputTokens = input;
            _pendingOutputTokens = output;
            _hasPendingUsage = true;
            return;
        }

        PrintUsage(input, output);
    }

    private void HandleComplete(RenderEvent _)
    {
        if (_currentState == RenderState.Responding && _responseBuffer.Length > 0)
        {
            _markdownStreamSession?.Complete();
            _markdownStreamSession = null;
            _responseBuffer.Clear();
            FlushPendingUsageIfAny();
            AnsiConsole.WriteLine();
            _currentState = RenderState.Idle;
            return;
        }

        CleanupCurrentState();
        FlushPendingUsageIfAny();
        AnsiConsole.WriteLine();
        _currentState = RenderState.Idle;
    }

    private void CleanupCurrentState()
    {
        if (_currentState == RenderState.Responding && _responseBuffer.Length > 0)
        {
            _markdownStreamSession?.Complete();
            _markdownStreamSession = null;
            AnsiConsole.WriteLine();
            _responseBuffer.Clear();
        }
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }

    private static string NormalizeInlineText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Status spinner is a single-line live render; embedded newlines
        // break cursor overwrite and cause line-by-line growth.
        return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
    }

    private static int GetStatusSegmentMaxLength()
    {
        try
        {
            // Keep each segment short enough to reduce terminal auto-wrap.
            // Reserve width for icon/title/spinner and markup overhead.
            var width = Console.WindowWidth;
            if (width <= 0)
            {
                return 60;
            }

            return Math.Clamp(width / 3, 24, 60);
        }
        catch
        {
            // Console width may be unavailable in some hosts.
            return 60;
        }
    }

    private static void PrintUsage(long inputTokens, long outputTokens)
    {
        AnsiConsole.MarkupLine($"[blue]↑ {inputTokens} input[/] [green]↓ {outputTokens} output[/]");
    }

    private void FlushPendingUsageIfAny()
    {
        if (!_hasPendingUsage)
        {
            return;
        }

        PrintUsage(_pendingInputTokens, _pendingOutputTokens);
        _hasPendingUsage = false;
        _pendingInputTokens = 0;
        _pendingOutputTokens = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _eventQueue.Writer.TryComplete();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();

        try
        {
            _renderTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore errors during disposal
        }
    }
}


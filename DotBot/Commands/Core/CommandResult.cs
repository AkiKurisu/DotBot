namespace DotBot.Commands.Core;

/// <summary>
/// Represents the result of a command execution.
/// </summary>
public sealed class CommandResult
{
    /// <summary>
    /// Whether the command was handled.
    /// </summary>
    public bool Handled { get; init; }
    
    /// <summary>
    /// Optional message to send back to the user.
    /// </summary>
    public string? Message { get; init; }
    
    /// <summary>
    /// Whether to send the message as markdown.
    /// </summary>
    public bool IsMarkdown { get; init; }
    
    /// <summary>
    /// Creates a result indicating the command was handled.
    /// </summary>
    public static CommandResult HandledResult(string? message = null, bool isMarkdown = false)
        => new() { Handled = true, Message = message, IsMarkdown = isMarkdown };
    
    /// <summary>
    /// Creates a result indicating the command was not handled.
    /// </summary>
    public static CommandResult NotHandled()
        => new() { Handled = false };
    
    /// <summary>
    /// Implicit conversion from bool to CommandResult.
    /// </summary>
    public static implicit operator CommandResult(bool handled)
        => handled ? HandledResult() : NotHandled();
}

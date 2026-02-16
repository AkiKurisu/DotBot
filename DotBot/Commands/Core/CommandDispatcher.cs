using DotBot.CLI;
using DotBot.Commands.Handlers;

namespace DotBot.Commands.Core;

/// <summary>
/// Dispatches commands to appropriate handlers.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _knownCommands = [];
    
    /// <summary>
    /// Gets all known command names.
    /// </summary>
    public IReadOnlyList<string> KnownCommands => _knownCommands;
    
    /// <summary>
    /// Registers a command handler.
    /// </summary>
    /// <param name="handler">The handler to register.</param>
    public void RegisterHandler(ICommandHandler handler)
    {
        foreach (var cmd in handler.Commands)
        {
            _handlers[cmd] = handler;
            if (!_knownCommands.Contains(cmd))
                _knownCommands.Add(cmd);
        }
    }
    
    /// <summary>
    /// Attempts to dispatch and handle a command.
    /// </summary>
    /// <param name="rawText">The raw input text.</param>
    /// <param name="context">The command context.</param>
    /// <param name="responder">The responder for sending messages.</param>
    /// <returns>True if the command was handled, false otherwise.</returns>
    public async Task<bool> TryDispatchAsync(string rawText, CommandContext context, ICommandResponder responder)
    {
        var trimmedText = rawText.Trim();
        if (!trimmedText.StartsWith('/'))
            return false;
        
        // Parse command and arguments
        var parts = trimmedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1..] : [];
        
        // Update context with parsed values
        context = context with
        {
            RawText = rawText,
            Command = cmd,
            Arguments = args
        };
        
        // Try to find handler
        if (_handlers.TryGetValue(cmd, out var handler))
        {
            var result = await handler.HandleAsync(context, responder);
            return result.Handled;
        }
        
        // Unknown command - format helpful message
        var msg = CommandHelper.FormatUnknownCommandMessage(rawText, _knownCommands.ToArray());
        await responder.SendTextAsync(msg);
        return true;
    }
    
    /// <summary>
    /// Creates a default dispatcher with all built-in handlers registered.
    /// </summary>
    public static CommandDispatcher CreateDefault()
    {
        var dispatcher = new CommandDispatcher();
        dispatcher.RegisterHandler(new NewCommandHandler());
        dispatcher.RegisterHandler(new DebugCommandHandler());
        dispatcher.RegisterHandler(new HelpCommandHandler());
        dispatcher.RegisterHandler(new HeartbeatCommandHandler());
        dispatcher.RegisterHandler(new CronCommandHandler());
        return dispatcher;
    }
}

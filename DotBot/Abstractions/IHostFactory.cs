using DotBot.Hosting;

namespace DotBot.Abstractions;

/// <summary>
/// Factory for creating DotBot hosts.
/// Each module can provide a host factory to create its specific host implementation.
/// </summary>
public interface IHostFactory
{
    /// <summary>
    /// Gets the mode name this factory creates hosts for (e.g., "cli", "api", "qq", "wecom").
    /// </summary>
    string ModeName { get; }

    /// <summary>
    /// Creates a host instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="context">The module context containing configuration and paths.</param>
    /// <returns>A host instance.</returns>
    IDotBotHost CreateHost(IServiceProvider serviceProvider, ModuleContext context);
}

using Microsoft.Extensions.DependencyInjection;

namespace DotBot.Abstractions;

/// <summary>
/// Base class for DotBot modules providing common functionality.
/// </summary>
public abstract class ModuleBase : IDotBotModule
{
    /// <inheritdoc />
    public virtual string Name => "";

    /// <inheritdoc />
    public virtual int Priority => 0;

    /// <inheritdoc />
    public abstract bool IsEnabled(AppConfig config);

    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // Default implementation does nothing.
        // Derived classes can override to register module-specific services.
    }
}

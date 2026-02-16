namespace DotBot.Modules;

/// <summary>
/// Marks a class as a DotBot module for automatic discovery by the source generator.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
public sealed class DotBotModuleAttribute : System.Attribute
{
    /// <summary>
    /// Gets the unique name of the module.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the priority of the module (higher = more important).
    /// Default is 0.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the description of the module.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DotBotModuleAttribute"/> class.
    /// </summary>
    /// <param name="name">The unique name of the module.</param>
    public DotBotModuleAttribute(string name)
    {
        Name = name;
        Priority = 0;
    }
}

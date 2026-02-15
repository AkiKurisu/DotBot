namespace DotBot.Attributes;

/// <summary>
/// Attribute to mark tool methods with icon information
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ToolIconAttribute : Attribute
{
    /// <summary>
    /// Icon to display for this tool
    /// </summary>
    public string Icon { get; set; } = string.Empty;
}

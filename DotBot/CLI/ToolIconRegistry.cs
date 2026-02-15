using System.Reflection;
using DotBot.Attributes;

namespace DotBot.CLI;

/// <summary>
/// Registry for managing tool icons from ToolIconAttribute
/// </summary>
public static class ToolIconRegistry
{
    private static readonly Dictionary<string, string> ToolIcons = new();
    
    private static readonly Lock LockObject = new();
    
    private static bool _initialized;
    
    private const string DefaultIcon = "🔧";

    /// <summary>
    /// Initialize registry by scanning tool class instances for ToolIconAttribute
    /// </summary>
    public static void Initialize(params object[] toolInstances)
    {
        if (_initialized)
        {
            return;
        }

        lock (LockObject)
        {
            if (_initialized)
            {
                return;
            }

            foreach (var toolInstance in toolInstances)
            {
                var toolType = toolInstance.GetType();
                var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

                foreach (var method in methods)
                {
                    var toolIconAttr = method.GetCustomAttribute<ToolIconAttribute>();
                    if (toolIconAttr == null || string.IsNullOrEmpty(toolIconAttr.Icon))
                    {
                        continue;
                    }

                    var pascalCaseName = method.Name;
                    
                    if (!string.IsNullOrEmpty(pascalCaseName))
                    {
                        ToolIcons[pascalCaseName] = toolIconAttr.Icon;
                    }
                }
            }

            _initialized = true;
        }
    }

    /// <summary>
    /// Get tool icon by tool name (supports both PascalCase and snake_case)
    /// </summary>
    public static string GetToolIcon(string toolName)
    {
        return ToolIcons.GetValueOrDefault(toolName, DefaultIcon);
    }

    public static void RegisterIcon(string toolName, string icon)
    {
        ToolIcons[toolName] = icon;
    }

    /// <summary>
    /// Check if tool icon is registered
    /// </summary>
    public static bool IsToolIconRegistered(string toolName)
    {
        return ToolIcons.ContainsKey(toolName);
    }

    /// <summary>
    /// Get all registered tool icons
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetAllToolIcons()
    {
        return ToolIcons;
    }
}

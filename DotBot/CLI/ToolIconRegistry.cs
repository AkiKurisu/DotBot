using System.Reflection;
using DotBot.Attributes;

namespace DotBot.CLI;

/// <summary>
/// Registry for managing tool icons from ToolIconAttribute.
/// Supports scanning assemblies to discover tool icons without creating tool instances.
/// </summary>
public static class ToolIconRegistry
{
    private static readonly Dictionary<string, string> ToolIcons = new();
    
    private static readonly Lock LockObject = new();
    
    private static readonly HashSet<Assembly> ScannedAssemblies = [];
    
    private const string DefaultIcon = "🔧";

    /// <summary>
    /// Scans an assembly for tool methods decorated with ToolIconAttribute.
    /// This method does not require creating tool instances.
    /// </summary>
    /// <param name="assembly">The assembly to scan for tool icons.</param>
    public static void ScanAssembly(Assembly assembly)
    {
        lock (LockObject)
        {
            if (ScannedAssemblies.Contains(assembly))
                return;
            
            ScannedAssemblies.Add(assembly);

            foreach (var type in assembly.GetTypes())
            {
                // Only scan non-abstract, non-generic class types
                if (type.IsAbstract || type.IsGenericTypeDefinition || !type.IsClass)
                    continue;

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);

                foreach (var method in methods)
                {
                    var toolIconAttr = method.GetCustomAttribute<ToolIconAttribute>();
                    if (toolIconAttr == null || string.IsNullOrEmpty(toolIconAttr.Icon))
                        continue;

                    ToolIcons[method.Name] = toolIconAttr.Icon;
                }
            }
        }
    }

    /// <summary>
    /// Scans multiple assemblies for tool icons.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    public static void ScanAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
            ScanAssembly(assembly);
    }

    /// <summary>
    /// Initialize registry by scanning tool class instances for ToolIconAttribute.
    /// Deprecated: Prefer using ScanAssembly() instead.
    /// </summary>
    [Obsolete("Use ScanAssembly() instead. This method is kept for backward compatibility.")]
    public static void Initialize(params object[] toolInstances)
    {
        foreach (var toolInstance in toolInstances)
        {
            ScanAssembly(toolInstance.GetType().Assembly);
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

    /// <summary>
    /// Resets the registry state. Used for testing purposes.
    /// </summary>
    internal static void Reset()
    {
        lock (LockObject)
        {
            ToolIcons.Clear();
            ScannedAssemblies.Clear();
        }
    }
}

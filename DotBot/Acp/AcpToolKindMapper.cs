namespace DotBot.Acp;

/// <summary>
/// Maps DotBot tool names to ACP tool call kinds.
/// </summary>
public static class AcpToolKindMapper
{
    private static readonly Dictionary<string, string> ToolKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["read_file"] = AcpToolKind.Read,
        ["ReadFile"] = AcpToolKind.Read,
        ["write_file"] = AcpToolKind.Edit,
        ["WriteFile"] = AcpToolKind.Edit,
        ["edit_file"] = AcpToolKind.Edit,
        ["EditFile"] = AcpToolKind.Edit,
        ["grep_files"] = AcpToolKind.Search,
        ["GrepFiles"] = AcpToolKind.Search,
        ["find_files"] = AcpToolKind.Search,
        ["FindFiles"] = AcpToolKind.Search,
        ["list_directory"] = AcpToolKind.Search,
        ["exec"] = AcpToolKind.Execute,
        ["Exec"] = AcpToolKind.Execute,
        ["web_search"] = AcpToolKind.Fetch,
        ["WebSearch"] = AcpToolKind.Fetch,
        ["web_fetch"] = AcpToolKind.Fetch,
        ["WebFetch"] = AcpToolKind.Fetch,
        ["spawn_subagent"] = AcpToolKind.Think,
        ["SpawnSubagent"] = AcpToolKind.Think
    };

    /// <summary>
    /// Gets the ACP tool kind for the given DotBot tool name.
    /// </summary>
    public static string GetKind(string toolName)
    {
        return ToolKindMap.GetValueOrDefault(toolName, AcpToolKind.Other);
    }

    /// <summary>
    /// Extracts file paths from tool call arguments for reporting file locations.
    /// </summary>
    public static List<string>? ExtractFilePaths(string toolName, IDictionary<string, object?>? arguments)
    {
        if (arguments == null) return null;

        var kind = GetKind(toolName);
        if (kind is not (AcpToolKind.Read or AcpToolKind.Edit or AcpToolKind.Delete))
            return null;

        var paths = new List<string>();
        foreach (var key in new[] { "path", "filePath", "file_path", "file" })
        {
            if (arguments.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s))
                paths.Add(s);
        }

        return paths.Count > 0 ? paths : null;
    }
}

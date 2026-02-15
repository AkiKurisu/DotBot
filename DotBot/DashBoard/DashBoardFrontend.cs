using System.Reflection;

namespace DotBot.DashBoard;

public static class DashBoardFrontend
{
    private static string? _cachedHtml;

    public static string GetHtml()
    {
        if (_cachedHtml != null) return _cachedHtml;

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("DotBot.Resources.DashBoard.html")
            ?? throw new InvalidOperationException("Embedded resource 'DotBot.Resources.DashBoard.html' not found.");
        using var reader = new StreamReader(stream);
        _cachedHtml = reader.ReadToEnd();
        return _cachedHtml;
    }
}


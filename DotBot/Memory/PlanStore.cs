using System.Text;

namespace DotBot.Memory;

/// <summary>
/// Persists plan files to disk. Each session gets one plan file that is
/// overwritten on every Plan-mode turn with the latest assistant output.
/// </summary>
public sealed class PlanStore(string botPath)
{
    private string PlansDir => Path.Combine(botPath, "plans");

    public string GetPlanPath(string sessionId)
        => Path.Combine(PlansDir, $"{sessionId}.md");

    public async Task SavePlanAsync(string sessionId, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        Directory.CreateDirectory(PlansDir);
        await File.WriteAllTextAsync(GetPlanPath(sessionId), content, Encoding.UTF8);
    }

    public async Task<string?> LoadPlanAsync(string sessionId)
    {
        var path = GetPlanPath(sessionId);
        if (!File.Exists(path))
            return null;

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    public bool PlanExists(string sessionId)
        => File.Exists(GetPlanPath(sessionId));

    public void DeletePlan(string sessionId)
    {
        var path = GetPlanPath(sessionId);
        if (File.Exists(path))
            File.Delete(path);
    }
}

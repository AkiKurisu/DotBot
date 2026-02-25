using DotBot.Memory;
using DotBot.Skills;

namespace DotBot.Context;

/// <summary>
/// Builds the complete system prompt from memory, skills, and configuration.
/// </summary>
public sealed class PromptBuilder(MemoryStore memoryStore, SkillsLoader skillsLoader, string cortexBotPath, string workspacePath, string baseInstructions)
{
    private readonly string _cortexBotPath = Path.GetFullPath(cortexBotPath);

    private readonly string _workspacePath = Path.GetFullPath(workspacePath);

    /// <summary>
    /// Bootstrap files to load from DotBot directory.
    /// </summary>
    private static readonly string[] BootstrapFiles =
    [
        "AGENTS.md",
        "SOUL.md",
        "USER.md",
        "TOOLS.md",
        "IDENTITY.md"
    ];

    /// <summary>
    /// Build the complete system prompt with identity, bootstrap files, memory, and skills.
    /// </summary>
    public string BuildSystemPrompt()
    {
        var parts = new List<string>
        {
            // Core identity & base instructions
            GetIdentity(),
            baseInstructions
        };

        // Bootstrap files (AGENTS.md, SOUL.md, USER.md, TOOLS.md, IDENTITY.md)
        var bootstrapContent = LoadBootstrapFiles();
        if (!string.IsNullOrWhiteSpace(bootstrapContent))
        {
            parts.Add(bootstrapContent);
        }

        // Memory context
        var memory = memoryStore.GetMemoryContext();
        if (!string.IsNullOrWhiteSpace(memory))
            parts.Add($"# Memory\n\n{memory}");

        // Skills - Progressive loading approach:
        // 1. Always-loaded skills: include full content
        var alwaysSkills = skillsLoader.GetAlwaysSkills();
        if (alwaysSkills.Count > 0)
        {
            var alwaysContent = skillsLoader.LoadSkillsForContext(alwaysSkills);
            if (!string.IsNullOrWhiteSpace(alwaysContent))
                parts.Add($"# Active Skills\n\n{alwaysContent}");
        }

        // 2. Available skills: show summary (agent uses ReadFile to load full content)
        var skillsSummary = skillsLoader.BuildSkillsSummary();
        if (!string.IsNullOrWhiteSpace(skillsSummary))
        {
            parts.Add(
$"""
# Skills

The following skills extend your capabilities. To use a skill, read its SKILL.md file using the ReadFile tool.

{skillsSummary}
"""
                );
        }

        foreach (var provider in ChatContextRegistry.All)
        {
            var section = provider.GetSystemPromptSection();
            if (!string.IsNullOrWhiteSpace(section))
                parts.Add(section);
        }

        return string.Join("\n\n---\n\n", parts);
    }

    /// <summary>
    /// Load bootstrap files from DotBot directory.
    /// Bootstrap files provide additional context and instructions.
    /// </summary>
    /// <returns>Combined content of all bootstrap files, or empty string if none exist.</returns>
    private string LoadBootstrapFiles()
    {
        var parts = new List<string>();

        foreach (var filename in BootstrapFiles)
        {
            var filePath = Path.Combine(_cortexBotPath, filename);
            if (File.Exists(filePath))
            {
                try
                {
                    var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        parts.Add($"## {filename}\n\n{content}");
                    }
                }
                catch (Exception ex)
                {
                    // Log warning but continue loading other files
                    Console.WriteLine($"[Warning] Failed to load bootstrap file {filename}: {ex.Message}");
                }
            }
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : string.Empty;
    }

    private string GetIdentity()
    {
        var workspace = _workspacePath;
        var dotBot = _cortexBotPath;

        return
$$"""
# DotBot 🤖

You are DotBot, a helpful AI assistant. You have access to tools that allow you to:
- Read, write, and edit files
- Execute shell commands
- Complete user tasks efficiently

## Workspace
Your workspace is at: {{workspace}}
This is your working directory where you perform file and shell operations.

## DotBot Directory
Your data directory is at: {{dotBot}}
This contains:
- Memory: {{dotBot}}/memory/ (see Memory skill for details)
- Custom skills: {{dotBot}}/skills/{skill-name}/SKILL.md
- Configuration: {{dotBot}}/appsettings.json
""";
    }
}

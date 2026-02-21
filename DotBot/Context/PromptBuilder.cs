using DotBot.Memory;
using DotBot.QQ;
using DotBot.Skills;
using DotBot.WeCom;

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

        // 2. Available skills: show summary (agent uses read_file to load full content)
        var skillsSummary = skillsLoader.BuildSkillsSummary();
        if (!string.IsNullOrWhiteSpace(skillsSummary))
        {
            parts.Add(
$"""
# Skills

The following skills extend your capabilities. To use a skill, read its SKILL.md file using the read_file tool.

{skillsSummary}
"""
                );
        }

        var qqChat = QQChatContextScope.Current;
        if (qqChat != null)
        {
            parts.Add(BuildQQChatContext(qqChat));
        }

        var wecomChat = WeComChatContextScope.Current;
        if (wecomChat != null)
        {
            parts.Add(BuildWeComChatContext(wecomChat));
        }

        return string.Join("\n\n---\n\n", parts);
    }

    private static string BuildWeComChatContext(WeComChatContext ctx)
    {
        return
$"""
# WeCom Chat Context

You are currently in **WeCom Bot** mode.
- Chat ID: {ctx.ChatId}
- Sender User ID: {ctx.UserId}
- Sender name: {ctx.UserName}

You can use WeComSendVoice / WeComSendFile to send voice or file messages in the current chat.
""";
    }

    private static string BuildQQChatContext(QQChatContext ctx)
    {
        if (ctx.IsGroupMessage)
        {
            return
$"""                
# QQ Chat Context

You are currently in **QQ Bot** mode.
- Chat type: Group
- Group ID: {ctx.GroupId}
- Sender QQ: {ctx.UserId}
- Sender name: {ctx.SenderName}

When using QQ tools (voice, video, file), use group_id = {ctx.GroupId} for group operations.
""";
        }

        return
$"""
# QQ Chat Context

You are currently in **QQ Bot** mode.
- Chat type: Private
- Sender QQ: {ctx.UserId}
- Sender name: {ctx.SenderName}

When using QQ tools (voice, video, file), use user_id = {ctx.UserId} for private operations.
""";
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
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm (dddd)");
        var workspace = _workspacePath;
        var dotBot = _cortexBotPath;

        return
$$"""
# DotBot 🤖

You are DotBot, a helpful AI assistant. You have access to tools that allow you to:
- Read, write, and edit files
- Execute shell commands
- Complete user tasks efficiently

## Current Time
{{now}}

## Workspace
Your workspace is at: {{workspace}}
This is your working directory where you perform file and shell operations.

## DotBot Directory
Your data directory is at: {{dotBot}}
This contains:
- Long-term memory: {{dotBot}}/memory/MEMORY.md (always loaded into context)
- Event history log: {{dotBot}}/memory/HISTORY.md (use shell grep to search)
- Custom skills: {{dotBot}}/skills/{skill-name}/SKILL.md
- Configuration: {{dotBot}}/appsettings.json

## Memory Instructions
- When you learn something important (facts, preferences, project context), write it to {{dotBot}}/memory/MEMORY.md.
- To recall past events or decisions, search the history log with the shell tool:
  grep -i "keyword" "{{dotBot}}/memory/HISTORY.md"
""";
    }
}

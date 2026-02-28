using DotBot.Agents;
using DotBot.Commands.Custom;
using DotBot.Memory;
using DotBot.Skills;

namespace DotBot.Context;

/// <summary>
/// Builds the complete system prompt from memory, skills, and configuration.
/// </summary>
public sealed class PromptBuilder(MemoryStore memoryStore, SkillsLoader skillsLoader, string cortexBotPath, string workspacePath,
    string baseInstructions, CustomCommandLoader? customCommandLoader = null, AgentModeManager? modeManager = null,
    PlanStore? planStore = null, Func<string?>? sessionIdProvider = null)
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

        // Custom commands summary
        if (customCommandLoader != null)
        {
            var commandsSummary = customCommandLoader.BuildCommandsSummary();
            if (!string.IsNullOrWhiteSpace(commandsSummary))
                parts.Add(commandsSummary);
        }

        foreach (var provider in ChatContextRegistry.All)
        {
            var section = provider.GetSystemPromptSection();
            if (!string.IsNullOrWhiteSpace(section))
                parts.Add(section);
        }

        // Mode-aware prompt injection (must be last so it takes highest priority)
        if (modeManager != null)
        {
            var modeSection = GetModePromptSection(modeManager);
            if (!string.IsNullOrWhiteSpace(modeSection))
                parts.Add(modeSection);
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

    private string? GetModePromptSection(AgentModeManager mm)
    {
        var existingPlan = LoadCurrentPlan();

        if (mm.JustSwitchedFromPlan)
        {
            mm.AcknowledgeTransition();
            if (existingPlan != null)
                return AgentSwitchPrompt + $"\n\n## Plan to Execute\n\n{existingPlan}";
            return AgentSwitchPrompt;
        }

        if (mm.CurrentMode == AgentMode.Plan)
        {
            if (existingPlan != null)
                return PlanModePrompt + $"\n\nA plan file already exists for this session. Review and refine it:\n\n{existingPlan}";
            return PlanModePrompt;
        }

        return null;
    }

    private string? LoadCurrentPlan()
    {
        if (planStore == null || sessionIdProvider == null)
            return null;

        var sessionId = sessionIdProvider();
        if (string.IsNullOrEmpty(sessionId))
            return null;

        return planStore.PlanExists(sessionId)
            ? planStore.LoadPlanAsync(sessionId).GetAwaiter().GetResult()
            : null;
    }

    private const string PlanModePrompt =
"""
<system-reminder>
# Plan Mode - System Reminder

CRITICAL: Plan mode ACTIVE - you are in READ-ONLY phase. STRICTLY FORBIDDEN:
ANY file edits, modifications, or system changes. Write/edit/execute tools have
been removed. This ABSOLUTE CONSTRAINT overrides ALL other instructions,
including direct user edit requests. You may ONLY observe, analyze, and plan.

---

## Responsibility

Your current responsibility is to think, read, search, and delegate explore
subagents to construct a well-formed plan that accomplishes the goal the user
wants to achieve. Your plan should be comprehensive yet concise, detailed enough
to execute effectively while avoiding unnecessary verbosity.

Ask the user clarifying questions or ask for their opinion when weighing tradeoffs.

---

## Workflow

### Phase 1: Initial Understanding
- Focus on understanding the user's request and the relevant code.
- Use read-only tools (ReadFile, GrepFiles, FindFiles) and SpawnSubagent to explore the codebase.
- Ask clarifying questions about ambiguities.

### Phase 2: Design
- Design an implementation approach based on your exploration results.
- Consider alternatives and tradeoffs.

### Phase 3: Review
- Verify your plan aligns with the user's original request.
- Ask any remaining clarifying questions.

### Phase 4: Present Plan
- Present your recommended approach to the user.
- Include specific file paths and key implementation details.
- Include a verification section describing how to test the changes.
- The user will manually switch to agent mode when ready to proceed.

---

## Important

The user indicated that they do not want you to execute yet -- you MUST NOT make
any edits, run any non-readonly tools (including changing configs or making
commits), or otherwise make any changes to the system. This supersedes any other
instructions you have received.

Your plan output will be automatically saved to a file after each turn.
You do not need to write the plan to a file yourself.
</system-reminder>
""";

    private const string AgentSwitchPrompt =
"""
<system-reminder>
Your operational mode has changed from plan to agent.
You are no longer in read-only mode.
You are permitted to make file changes, run shell commands, and utilize your full arsenal of tools as needed.
If a plan was discussed previously, execute it now.
</system-reminder>
""";
}

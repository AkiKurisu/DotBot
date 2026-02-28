using System.Text.Json;
using DotBot.Agents;
using DotBot.Commands.Custom;
using DotBot.DashBoard;
using DotBot.Memory;
using DotBot.Skills;
using Microsoft.Agents.AI;

namespace DotBot.Context;

/// <summary>
/// Enhanced context provider combining memory, skills, and system prompt.
/// </summary>
public sealed class MemoryContextProvider(
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    string cortexBotPath,
    string workspacePath,
    string baseInstructions,
    TraceCollector? traceCollector = null,
    Func<IReadOnlyList<string>>? toolNamesProvider = null,
    CustomCommandLoader? customCommandLoader = null,
    AgentModeManager? modeManager = null,
    PlanStore? planStore = null,
    Func<string?>? sessionIdProvider = null)
    : AIContextProvider
{
    private readonly PromptBuilder _promptBuilder = new(memoryStore, skillsLoader, cortexBotPath, workspacePath, baseInstructions, customCommandLoader, modeManager, planStore, sessionIdProvider);

    protected override ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var systemPrompt = _promptBuilder.BuildSystemPrompt();
        var sessionKey = TracingChatClient.CurrentSessionKey;
        if (!string.IsNullOrWhiteSpace(sessionKey))
            traceCollector?.RecordSessionMetadata(sessionKey, systemPrompt, toolNamesProvider?.Invoke());

        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = systemPrompt
        });
    }

    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        return JsonSerializer.SerializeToElement(new ContextSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow
        }, jsonSerializerOptions);
    }

    private sealed class ContextSnapshot
    {
        public DateTimeOffset Timestamp { get; set; }
    }
}

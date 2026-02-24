using System.Text;
using DotBot.Agents;
using DotBot.CLI.Rendering;
using DotBot.Configuration;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Heartbeat;
using DotBot.Localization;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.Security;
using DotBot.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotBot.CLI;

public sealed class ReplHost(AIAgent agent, SessionStore sessionStore, SkillsLoader skillsLoader, 
    string workspacePath = "", string cortexBotPath = "", AppConfig? config = null, 
    HeartbeatService? heartbeatService = null, CronService? cronService = null,
    AgentFactory? agentFactory = null, McpClientManager? mcpClientManager = null,
    string? dashBoardUrl = null,
    LanguageService? languageService = null, TokenUsageStore? tokenUsageStore = null)
{
    private readonly AppConfig _config = config ?? new AppConfig();
    private readonly LanguageService _lang = languageService ?? new LanguageService();

    private string _currentSessionId = string.Empty;
    
    private AgentSession _agentSession = null!;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Generate a new session ID on startup
        _currentSessionId = SessionStore.GenerateSessionId();
        ShowWelcomeScreen(_currentSessionId);

        // Load or create session
        _agentSession = await sessionStore.LoadOrCreateAsync(agent, _currentSessionId, cancellationToken);

        while (true)
        {
            var input = ReadInput(_currentSessionId);
            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var trimmed = input.Trim();
            var (handled, shouldExit) = await HandleCommand(trimmed);
            if (handled)
            {
                if (shouldExit)
                {
                    break;
                }
                continue;
            }

            if (await RunStreamingAsync(trimmed, _agentSession, cancellationToken))
            {
                await TryCompactContextAsync(_currentSessionId, _agentSession, cancellationToken);
                agentFactory?.TryConsolidateMemory(_agentSession, _currentSessionId);
                await sessionStore.SaveAsync(agent, _agentSession, _currentSessionId, cancellationToken);
            }
        }

        AnsiConsole.MarkupLine($"\n[blue]👋 {Strings.Goodbye(_lang)}[/]");
    }

    private static string? ReadInput(string currentSessionId = "")
    {
        PrintPrompt(currentSessionId);
        return Console.ReadLine();
    }

    private static void PrintPrompt(string currentSessionId)
    {
        var sessionDisplay = string.IsNullOrEmpty(currentSessionId) ? "" : $"[grey]({currentSessionId.EscapeMarkup()})[/]";
        AnsiConsole.Markup($"[green]{sessionDisplay}> [/]");
    }

    public void ReprintPrompt()
    {
        AnsiConsole.WriteLine();
        PrintPrompt(_currentSessionId);
    }

    private void ShowWelcomeScreen(string currentSessionId)
    {
        StatusPanel.ShowWelcome(currentSessionId, dashBoardUrl, _lang);
    }

    private async Task LoadSessionAsync(string newSessionId, CancellationToken cancellationToken)
    {
        try
        {
            // Load new session
            _agentSession = await sessionStore.LoadOrCreateAsync(agent, newSessionId, cancellationToken);
            _currentSessionId = newSessionId;

            // Refresh display
            AnsiConsole.Clear();
            ShowWelcomeScreen(_currentSessionId);
            
            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionLoaded(_lang)}：[cyan]{newSessionId.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionLoadFailed(_lang)}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Create a new session.
    /// </summary>
    private async Task NewSession(CancellationToken cancellationToken)
    {
        try
        {
            // Generate new session ID and create session
            var newSessionId = SessionStore.GenerateSessionId();
            _agentSession = await sessionStore.LoadOrCreateAsync(agent, newSessionId, cancellationToken);
            _currentSessionId = newSessionId;

            // Refresh display
            AnsiConsole.Clear();
            ShowWelcomeScreen(_currentSessionId);

            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionCreated(_lang)}：[cyan]{newSessionId.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionCreateFailed(_lang)}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    private async Task DeleteSession(string sessionId)
    {
        try
        {
            var wasCurrent = sessionId == _currentSessionId;

            // Delete session
            var sessionDeleted = sessionStore.Delete(sessionId);

            if (sessionDeleted)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] {Strings.SessionDeleted(_lang)}：[cyan]{sessionId.EscapeMarkup()}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]{Strings.SessionNotFound(_lang)}：{sessionId.EscapeMarkup()}[/]");
            }

            // If deleted current session, create a new session
            if (wasCurrent)
            {
                _currentSessionId = SessionStore.GenerateSessionId();
                _agentSession = await sessionStore.LoadOrCreateAsync(agent, _currentSessionId, CancellationToken.None);
                AnsiConsole.MarkupLine($"[grey]→ {Strings.SessionNewCreated(_lang)}：{_currentSessionId.EscapeMarkup()}[/]");
            }

            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.SessionDeleteFailed(_lang)}：{ex.Message.EscapeMarkup()}");
            AnsiConsole.WriteLine();
        }
    }

    private static readonly string[] KnownCommands =
    [
        "/exit", "/help", "/clear", "/new", "/load", "/delete",
        "/init", "/skills", "/mcp", "/sessions", "/memory",
        "/config", "/debug", "/heartbeat", "/cron", "/lang"
    ];

    private async Task<(bool Handled, bool ShouldExit)> HandleCommand(string input)
    {
        switch (input.ToLowerInvariant())
        {
            case "/exit":
                return (true, true);

            case "/help":
                StatusPanel.ShowHelp(_lang);
                return (true, false);

            case "/clear":
                AnsiConsole.Clear();
                ShowWelcomeScreen(_currentSessionId);
                return (true, false);

            case "/new":
                await NewSession(CancellationToken.None);
                return (true, false);

            case "/load":
                var sessions = sessionStore.ListSessions();
                var selectedSession = SessionPrompt.SelectSessionToLoad(sessions, _currentSessionId);
                if (selectedSession != null)
                {
                    await LoadSessionAsync(selectedSession, CancellationToken.None);
                }
                return (true, false);

            case "/delete":
                var sessionsToDelete = sessionStore.ListSessions();
                var sessionToDelete = SessionPrompt.SelectSessionToDelete(sessionsToDelete, _currentSessionId);
                if (sessionToDelete != null)
                {
                    if (SessionPrompt.ConfirmDelete(sessionToDelete, sessionToDelete == _currentSessionId))
                    {
                        await DeleteSession(sessionToDelete);
                        AnsiConsole.Clear();
                        ShowWelcomeScreen(_currentSessionId);
                    }
                }
                return (true, false);

            case "/init":
                HandleInitCommand();
                return (true, false);

            case "/skills":
                var allSkills = skillsLoader.ListSkills(filterUnavailable: false);
                StatusPanel.ShowSkillsTable(allSkills, skillsLoader.WorkspaceSkillsPath, skillsLoader.UserSkillsPath, _lang);
                return (true, false);

            case "/mcp":
                StatusPanel.ShowMcpServersTable(mcpClientManager, _lang);
                return (true, false);

            case "/sessions":
                var sessionList = sessionStore.ListSessions();
                StatusPanel.ShowSessionsTable(sessionList, _lang);
                return (true, false);

            case "/memory":
                HandleMemoryCommand();
                return (true, false);

            case "/config":
                HandleConfigCommand();
                return (true, false);

            case "/debug":
                HandleDebugCommand();
                return (true, false);

            case "/lang":
                HandleLanguageCommand();
                return (true, false);
        }

        if (input.StartsWith("/heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            await HandleHeartbeatCommandAsync(input);
            return (true, false);
        }

        if (input.StartsWith("/cron", StringComparison.OrdinalIgnoreCase))
        {
            HandleCronCommand(input);
            return (true, false);
        }

        if (input.StartsWith('/'))
        {
            var msg = CommandHelper.FormatUnknownCommandMessage(input, KnownCommands, _lang);
            AnsiConsole.MarkupLine($"[red]{msg.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
            return (true, false);
        }

        return (false, false);
    }

    private void HandleInitCommand()
    {
        AnsiConsole.MarkupLine($"\n[blue]🚀 {Strings.InitWorkspace(_lang)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Strings.CurrentWorkspace(_lang)}: {workspacePath.EscapeMarkup()}[/]");

        if (Directory.Exists(workspacePath))
        {
            var confirm = AnsiConsole.Prompt(
                new ConfirmationPrompt(Strings.WorkspaceExists(_lang))
                {
                    DefaultValue = false
                });

            if (!confirm)
            {
                AnsiConsole.MarkupLine($"[grey]{Strings.InitCancelled(_lang)}[/]\n");
                return;
            }
        }

        var result = InitHelper.InitializeWorkspace(workspacePath);

        if (result == 0)
        {
            AnsiConsole.MarkupLine($"[green]✓[/] {Strings.InitComplete(_lang)}\n");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Strings.InitFailed(_lang)}: {result}\n");
        }
    }

    private void HandleConfigCommand()
    {
        AnsiConsole.MarkupLine($"\n[blue]📋 {Strings.CurrentConfig(_lang)}[/]");
        var configPath = Path.Combine(cortexBotPath, "appsettings.json");

        // Show current config
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn($"[grey]{Strings.ConfigItem(_lang)}[/]");
        table.AddColumn("[grey]值[/]");

        table.AddRow("API Key", string.IsNullOrWhiteSpace(_config.ApiKey) ? $"[red]{Strings.NotConfigured(_lang)}[/]" : $"[green]{Strings.Configured(_lang)}[/]");
        table.AddRow("Model", _config.Model.EscapeMarkup());
        table.AddRow("Endpoint", _config.EndPoint.EscapeMarkup());
        table.AddRow("System Instructions", 
            _config.SystemInstructions.Length > 50 
                ? _config.SystemInstructions.Substring(0, 50) + "..." 
                : _config.SystemInstructions.EscapeMarkup());

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[grey]{Strings.ConfigFilePath(_lang)}: {configPath.EscapeMarkup()}[/]");

        // Offer to edit
        AnsiConsole.MarkupLine($"\n[yellow]{Strings.ConfigTip(_lang)}[/]: {Strings.ConfigEditTip(_lang)}");
        AnsiConsole.WriteLine();
    }

    private void HandleMemoryCommand()
    {
        AnsiConsole.MarkupLine($"\n[purple]🧠 {Strings.LongTermMemory(_lang)}[/]");
        var memoryDir = Path.Combine(cortexBotPath, "memory");
        var memoryPath = Path.Combine(memoryDir, "MEMORY.md");

        if (!File.Exists(memoryPath))
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.MemoryNotExists(_lang)}[/]");
            AnsiConsole.MarkupLine($"[grey]{Strings.ExpectedPath(_lang)}: {memoryPath.EscapeMarkup()}[/]");
        }
        else
        {
            var content = File.ReadAllText(memoryPath, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(content))
            {
                AnsiConsole.MarkupLine($"[grey]{Strings.MemoryEmpty(_lang)}[/]");
            }
            else
            {
                var panel = new Panel(Markup.Escape(content))
                {
                    Header = new PanelHeader($"[purple]🗃️ {memoryPath.EscapeMarkup()}[/]"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Purple),
                    Expand = true
                };
                AnsiConsole.Write(panel);
            }
        }

        // Show HISTORY.md summary
        var historyPath = Path.Combine(memoryDir, "HISTORY.md");
        if (File.Exists(historyPath))
        {
            var historyInfo = new FileInfo(historyPath);
            var historyContent = File.ReadAllText(historyPath, Encoding.UTF8);
            var entryCount = historyContent.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Length;

            // Show last entry as preview
            var entries = historyContent.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            var lastEntry = entries.Length > 0 ? entries[^1].Trim() : string.Empty;
            var preview = lastEntry.Length > 200 ? lastEntry[..200] + "..." : lastEntry;

            var historyPanel = new Panel(
                string.IsNullOrWhiteSpace(preview) ? "[grey](no entries)[/]" : Markup.Escape(preview))
            {
                Header = new PanelHeader($"[blue]📜 HISTORY.md — {entryCount} entries, {historyInfo.Length / 1024.0:F1} KB[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue),
                Expand = true
            };
            AnsiConsole.Write(historyPanel);
            AnsiConsole.MarkupLine($"[grey]  Search: grep -i \"keyword\" \"{historyPath.EscapeMarkup()}\"[/]");
        }

        AnsiConsole.WriteLine();
    }

    private void HandleDebugCommand()
    {
        var newState = DebugModeService.Toggle();
        var statusMsg = newState 
            ? $"[green]✓[/] {Strings.DebugEnabled(_lang)}" 
            : $"[green]✓[/] {Strings.DebugDisabled(_lang)}";
        
        AnsiConsole.MarkupLine($"\n{statusMsg}\n");
    }

    private void HandleLanguageCommand()
    {
        var newLang = _lang.ToggleLanguage();
        var langName = newLang == Language.Chinese 
            ? Strings.LanguageChinese(_lang) 
            : Strings.LanguageEnglish(_lang);
        AnsiConsole.MarkupLine($"\n[green]✓[/] {Strings.LanguageSwitched(_lang)}: [cyan]{langName}[/]\n");
    }

    private async Task HandleHeartbeatCommandAsync(string input)
    {
        if (heartbeatService == null)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.HeartbeatUnavailable(_lang)}[/]\n");
            return;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 1 ? parts[1].ToLowerInvariant() : "trigger";

        switch (subCmd)
        {
            case "trigger":
                AnsiConsole.MarkupLine($"[blue]{Strings.TriggeringHeartbeat(_lang)}[/]");
                var result = await heartbeatService.TriggerNowAsync();
                if (result != null)
                    AnsiConsole.MarkupLine($"[green]{Strings.HeartbeatResult(_lang)}：[/] {Markup.Escape(result)}");
                else
                    AnsiConsole.MarkupLine($"[grey]{Strings.HeartbeatNoResponse(_lang)}[/]");
                break;
            default:
                AnsiConsole.MarkupLine($"[yellow]{Strings.HeartbeatUsage(_lang)}[/]");
                break;
        }
        AnsiConsole.WriteLine();
    }

    private void HandleCronCommand(string input)
    {
        if (cronService == null)
        {
            AnsiConsole.MarkupLine($"[yellow]{Strings.CronUnavailable(_lang)}[/]\n");
            return;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subCmd = parts.Length > 1 ? parts[1].ToLowerInvariant() : "list";

        switch (subCmd)
        {
            case "list":
            {
                var jobs = cronService.ListJobs(includeDisabled: true);
                if (jobs.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[grey]{Strings.NoCronJobs(_lang)}[/]");
                }
                else
                {
                    var table = new Table();
                    table.Border(TableBorder.Rounded);
                    table.AddColumn(Strings.CronColId(_lang));
                    table.AddColumn(Strings.CronColName(_lang));
                    table.AddColumn(Strings.CronColSchedule(_lang));
                    table.AddColumn(Strings.CronColStatus(_lang));
                    table.AddColumn(Strings.CronColNextRun(_lang));

                    foreach (var job in jobs)
                    {
                        var schedDesc = job.Schedule.Kind switch
                        {
                            "at" when job.Schedule.AtMs.HasValue =>
                                $"{Strings.CronExecuteOnce(_lang)} {DateTimeOffset.FromUnixTimeMilliseconds(job.Schedule.AtMs.Value):u} {Strings.CronExecuteOnceSuffix(_lang)}",
                            "every" when job.Schedule.EveryMs.HasValue =>
                                $"{Strings.CronEvery(_lang)} {TimeSpan.FromMilliseconds(job.Schedule.EveryMs.Value)}",
                            _ => job.Schedule.Kind
                        };
                        var next = job.State.NextRunAtMs.HasValue
                            ? DateTimeOffset.FromUnixTimeMilliseconds(job.State.NextRunAtMs.Value).ToString("u")
                            : "-";
                        var status = job.Enabled ? $"[green]{Strings.CronEnabled(_lang)}[/]" : $"[grey]{Strings.CronDisabled(_lang)}[/]";
                        table.AddRow(
                            Markup.Escape(job.Id),
                            Markup.Escape(job.Name),
                            Markup.Escape(schedDesc),
                            status,
                            Markup.Escape(next));
                    }
                    AnsiConsole.Write(table);
                }
                break;
            }
            case "remove":
            {
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronRemoveUsage(_lang)}[/]");
                    break;
                }
                var jobId = parts[2];
                if (cronService.RemoveJob(jobId))
                    AnsiConsole.MarkupLine($"[green]{Strings.CronJobDeleted(_lang)} '{Markup.Escape(jobId)}' {Strings.CronJobDeletedSuffix(_lang)}[/]");
                else
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronJobNotFound(_lang)} '{Markup.Escape(jobId)}'。[/]");
                break;
            }
            case "enable":
            case "disable":
            {
                if (parts.Length < 3)
                {
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronToggleUsage(_lang)}：/cron {subCmd} <jobId>[/]");
                    break;
                }
                var jobId = parts[2];
                var enabled = subCmd == "enable";
                var job = cronService.EnableJob(jobId, enabled);
                if (job != null)
                    AnsiConsole.MarkupLine($"[green]{Strings.CronJobDeleted(_lang)} '{Markup.Escape(jobId)}' {(enabled ? Strings.CronJobEnabled(_lang) : Strings.CronJobDisabled(_lang))}[/]");
                else
                    AnsiConsole.MarkupLine($"[yellow]{Strings.CronJobNotFound(_lang)} '{Markup.Escape(jobId)}'。[/]");
                break;
            }
            default:
                AnsiConsole.MarkupLine($"[yellow]{Strings.CronUsage(_lang)}[/]");
                break;
        }
        AnsiConsole.WriteLine();
    }

    private async Task TryCompactContextAsync(string sessionId, AgentSession session, CancellationToken cancellationToken)
    {
        if (agentFactory?.Compactor == null || agentFactory.MaxContextTokens <= 0)
            return;

        var tracker = agentFactory.GetOrCreateTokenTracker(sessionId);
        if (tracker.LastInputTokens < agentFactory.MaxContextTokens)
            return;

        AnsiConsole.MarkupLine($"[yellow]{Strings.ContextLimitReached(_lang)}[/]");
        var compacted = await agentFactory.Compactor.TryCompactAsync(session, cancellationToken);
        if (compacted)
        {
            tracker.Reset();
            AnsiConsole.MarkupLine($"[green]{Strings.ContextCompacted(_lang)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.ContextCompactSkipped(_lang)}[/]");
        }
    }

    private async Task<bool> RunStreamingAsync(
        string userInput,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create renderer and start it
            using var renderer = new AgentRenderer();
            await renderer.StartAsync(cancellationToken);

            // Set render control for approval service (thread-local)
            ConsoleApprovalService.SetRenderControl(renderer);

            try
            {
                var tokenTracker = agentFactory?.GetOrCreateTokenTracker(_currentSessionId);

                TracingChatClient.CurrentSessionKey = _currentSessionId;
                TracingChatClient.ResetCallState(_currentSessionId);
                long inputTokens = 0, outputTokens = 0;

                // Get streaming updates from agent
                var stream = agent.RunStreamingAsync(userInput, session, cancellationToken: cancellationToken);

                // Adapt stream to render events
                var events = StreamAdapter.AdaptAsync(WrapStream(stream), cancellationToken, tokenTracker);
                
                // Consume events through renderer
                await renderer.ConsumeEventsAsync(events, cancellationToken);
                
                // Wait for renderer to finish
                await renderer.StopAsync();

                if (inputTokens > 0 || outputTokens > 0)
                {
                    tokenUsageStore?.Record(new TokenUsageRecord
                    {
                        Source = TokenUsageSource.Cli,
                        UserId = "local",
                        DisplayName = "CLI",
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens
                    });
                }

                return true;

                async IAsyncEnumerable<AgentResponseUpdate> WrapStream(IAsyncEnumerable<AgentResponseUpdate> source)
                {
                    await foreach (var update in source.WithCancellation(cancellationToken))
                    {
                        foreach (var content in update.Contents)
                        {
                            if (content is UsageContent usage)
                            {
                                if (usage.Details.InputTokenCount.HasValue)
                                    inputTokens = usage.Details.InputTokenCount.Value;
                                if (usage.Details.OutputTokenCount.HasValue)
                                    outputTokens = usage.Details.OutputTokenCount.Value;
                            }
                        }
                        yield return update;
                    }
                }
            }
            finally
            {
                TracingChatClient.ResetCallState(_currentSessionId);
                TracingChatClient.CurrentSessionKey = null;
                // Clear render control
                ConsoleApprovalService.SetRenderControl(null);
            }
        }
        catch (Exception ex)
        {
            MessageFormatter.Error(ex.Message);
            return false;
        }
    }
}

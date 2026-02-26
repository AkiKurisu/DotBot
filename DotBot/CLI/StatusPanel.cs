using DotBot.Localization;
using DotBot.Mcp;
using DotBot.Memory;
using Spectre.Console;
using static DotBot.Skills.SkillsLoader;

namespace DotBot.CLI;

public static class StatusPanel
{
    public static void ShowWelcome(string? currentSessionId = null, string? dashBoardUrl = null, LanguageService? lang = null)
    {
        lang ??= new LanguageService();
        
        AnsiConsole.Clear();

        AnsiConsole.Write(
            new FigletText("DotBot")
                .LeftJustified()
                .Color(Color.Blue));
        if (!string.IsNullOrEmpty(currentSessionId))
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.CurrentSession(lang)}：[cyan]{currentSessionId.Escape()}[/][/]");
        }
        if (!string.IsNullOrEmpty(dashBoardUrl))
        {
            var escapedUrl = dashBoardUrl.Escape();
            AnsiConsole.MarkupLine($"[green]DashBoard:[/] [link={escapedUrl}]{escapedUrl}[/]");
        }
        AnsiConsole.WriteLine();

        // Quick command tips
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(
            new Markup("[blue]/exit[/]"),
            new Markup($"[grey]{Strings.CmdExit(lang)}[/]"),
            new Markup("[blue]/help[/]"),
            new Markup($"[grey]{Strings.CmdHelp(lang)}[/]"),
            new Markup("[blue]/new[/]"),
            new Markup($"[grey]{Strings.CmdNew(lang)}[/]"));
        grid.AddRow(
            new Markup("[blue]/load[/]"),
            new Markup($"[grey]{Strings.CmdLoad(lang)}[/]"),
            new Markup("[blue]/config[/]"),
            new Markup($"[grey]{Strings.CmdConfig(lang)}[/]"),
            new Markup("[blue]/commands[/]"),
            new Markup($"[grey]{Strings.CmdCommands(lang)}[/]"));

        var panel = new Panel(grid)
        {
            Header = new PanelHeader($"[yellow]💡 {Strings.QuickCommands(lang)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }
    
    public static void ShowSkillsTable(List<SkillInfo> skills, string? workspaceSkillsPath = null, string? userSkillsPath = null, LanguageService? lang = null)
    {
        lang ??= new LanguageService();
        
        if (skills.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]📝 {Strings.NoSkills(lang)}[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        table.AddColumn(new TableColumn($"[u]{Strings.Skill(lang)}[/]").Width(20));
        table.AddColumn(new TableColumn($"[u]{Strings.Status(lang)}[/]").Width(12));
        table.AddColumn(new TableColumn($"[u]{Strings.Source(lang)}[/]").Width(12));
        table.AddColumn(new TableColumn($"[u]{Strings.Description(lang)}[/]"));

        foreach (var skill in skills)
        {
            var status = skill.Available
                ? $"[green]✓ {Strings.Available(lang)}[/]"
                : $"[red]✗ {skill.UnavailableReason?.Escape() ?? Strings.Unavailable(lang)}[/]";

            var (sourceColor, sourceLabel) = skill.Source switch
            {
                "workspace" => ("blue", "workspace"),
                "builtin" => ("yellow", "builtin"),
                _ => ("grey", skill.Source)
            };
            var source = $"[{sourceColor}]{sourceLabel}[/]";

            var description = GetSkillDescription(skill, lang).Escape();

            table.AddRow(
                $"[white]{skill.Name.Escape()}[/]",
                status,
                source,
                $"[grey]{description}[/]");
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[blue]📚 {Strings.AvailableSkills(lang)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue)
        };

        AnsiConsole.Write(panel);

        // Show paths
        if (!string.IsNullOrEmpty(workspaceSkillsPath) || !string.IsNullOrEmpty(userSkillsPath))
        {
            AnsiConsole.WriteLine();
            var pathTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Expand();

            pathTable.AddColumn(new TableColumn("[u]类型[/]").Width(15));
            pathTable.AddColumn(new TableColumn("[u]路径[/]"));

            if (!string.IsNullOrEmpty(workspaceSkillsPath))
            {
                pathTable.AddRow(
                    "[blue]Workspace[/]",
                    $"[grey]{workspaceSkillsPath.Escape()}[/]");
            }

            if (!string.IsNullOrEmpty(userSkillsPath))
            {
                pathTable.AddRow(
                    "[grey]User[/]",
                    $"[grey]{userSkillsPath.Escape()}[/]");
            }

            var pathPanel = new Panel(pathTable)
            {
                Header = new PanelHeader($"[yellow]📁 {Strings.SkillsPath(lang)}[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Yellow)
            };

            AnsiConsole.Write(pathPanel);
        }
    }
    
    public static void ShowSessionsTable(List<SessionStore.SessionInfo> sessions, LanguageService? lang = null)
    {
        lang ??= new LanguageService();
        
        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]💬 {Strings.NoSessions(lang)}[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        table.AddColumn(new TableColumn($"[u]{Strings.Session(lang)}[/]").Width(30));
        table.AddColumn(new TableColumn($"[u]{Strings.CreatedAt(lang)}[/]").Width(20));
        table.AddColumn(new TableColumn($"[u]{Strings.UpdatedAt(lang)}[/]").Width(20));

        foreach (var session in sessions)
        {
            var key = session.Key.Escape();
            var created = DateTimeOffset.Parse(session.CreatedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var updated = DateTimeOffset.Parse(session.UpdatedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            table.AddRow(
                $"[white]{key}[/]",
                $"[grey]{created}[/]",
                $"[grey]{updated}[/]");
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[green]💬 {Strings.SavedSessions(lang)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green)
        };

        AnsiConsole.Write(panel);
    }

    public static void ShowHelp(LanguageService? lang = null)
    {
        lang ??= new LanguageService();
        
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(new Markup($"[yellow]{Strings.Commands(lang)}:[/]"), new Markup(""));
        grid.AddRow("  /exit", Strings.CmdExit(lang));
        grid.AddRow("  /help", Strings.CmdHelp(lang));
        grid.AddRow("  /clear", Strings.CmdClear(lang));
        grid.AddRow("  /new", Strings.CmdNew(lang));
        grid.AddRow("  /load", Strings.CmdLoad(lang));
        grid.AddRow("  /delete", Strings.CmdDelete(lang));
        grid.AddRow("  /init", Strings.CmdInit(lang));
        grid.AddRow("  /config", Strings.CmdConfig(lang));
        grid.AddRow("  /debug", Strings.CmdDebug(lang));
        grid.AddRow("  /skills", Strings.CmdSkills(lang));
        grid.AddRow("  /mcp", Strings.CmdMcp(lang));
        grid.AddRow("  /sessions", Strings.CmdSessions(lang));
        grid.AddRow("  /memory", Strings.CmdMemory(lang));
        grid.AddRow("  /lang", Strings.CmdLang(lang));
        grid.AddRow("  /heartbeat trigger", Strings.CmdHeartbeat(lang));
        grid.AddRow("  /cron list", Strings.CmdCronList(lang));
        grid.AddRow("  /cron remove <id>", Strings.CmdCronRemove(lang));
        grid.AddRow("  /cron enable|disable <id>", Strings.CmdCronToggle(lang));
        grid.AddRow("  /commands", Strings.CmdCommands(lang));
        grid.AddRow("", "");
        grid.AddRow(new Markup($"[yellow]{Strings.UsageTips(lang)}:[/]"), new Markup(""));
        grid.AddRow($"  • {Strings.TipDirectInput(lang)}", "");
        grid.AddRow($"  • {Strings.TipArrowKeys(lang)}", "");
        grid.AddRow($"  • {Strings.TipAutoSave(lang)}", "");

        var panel = new Panel(grid)
        {
            Header = new PanelHeader($"[blue]❓ {Strings.CmdHelp(lang)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue)
        };

        AnsiConsole.Write(panel);
    }

    public static void ShowMcpServersTable(McpClientManager? mcpManager, LanguageService? lang = null)
    {
        lang ??= new LanguageService();
        
        if (mcpManager == null || mcpManager.Tools.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.NoMcpServers(lang)}[/]");
            AnsiConsole.MarkupLine($"[grey]{Strings.McpConfigTip(lang)}[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var serverTools = new Dictionary<string, List<string>>();
        foreach (var tool in mcpManager.Tools)
        {
            var serverName = mcpManager.ToolServerMap.GetValueOrDefault(tool.Name, Strings.Unknown(lang));
            if (!serverTools.TryGetValue(serverName, out var list))
            {
                list = [];
                serverTools[serverName] = list;
            }
            list.Add(tool.Name);
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        table.AddColumn(new TableColumn($"[u]{Strings.Server(lang)}[/]").Width(20));
        table.AddColumn(new TableColumn($"[u]{Strings.Tools(lang)}[/]").Width(10));
        table.AddColumn(new TableColumn($"[u]{Strings.ToolNames(lang)}[/]"));

        foreach (var (server, tools) in serverTools)
        {
            table.AddRow(
                $"[white]{server.Escape()}[/]",
                $"[cyan]{tools.Count}[/]",
                $"[grey]{string.Join(", ", tools.Select(t => t.Escape()))}[/]");
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[blue]{Strings.McpServices(lang)}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static string GetSkillDescription(SkillInfo skill, LanguageService? lang = null)
    {
        lang ??= new LanguageService();
        
        try
        {
            if (File.Exists(skill.Path))
            {
                var content = File.ReadAllText(skill.Path);
                var lines = content.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("description:"))
                    {
                        return line["description:".Length..].Trim().Trim('"', '\'');
                    }
                }
            }
        }
        catch
        {
            // ignored
        }

        return Strings.NoDescription(lang);
    }

    private static string Escape(this string text)
    {
        return Markup.Escape(text);
    }
}

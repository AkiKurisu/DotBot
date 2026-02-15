using System.Text;
using DotBot;
using DotBot.CLI;
using DotBot.Hosting;
using DotBot.Localization;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

var workspacePath = Directory.GetCurrentDirectory();
var botPath = Path.GetFullPath(".bot");

if (!Directory.Exists(botPath))
{
    // First, select language
    var selectedLanguage = InitHelper.SelectLanguage();
    var lang = new LanguageService(selectedLanguage);
    
    Console.WriteLine();
    AnsiConsole.MarkupLine($"[yellow]⚠️  {lang.GetString("DotBot 工作区不存在", "DotBot workspace not found")}: {Markup.Escape(botPath)}[/]");
    Console.WriteLine();
    AnsiConsole.WriteLine(lang.GetString(
        "DotBot 需要一个工作区来存储会话、记忆和配置。",
        "DotBot needs a workspace to store sessions, memory, and configuration."));
    Console.WriteLine();
    
    AnsiConsole.WriteLine();
    
    if (InitHelper.AskYesNo(
        lang.GetString("是否现在创建 DotBot 工作区并初始化? (Y/n)", 
                      "Create and initialize DotBot workspace now? (Y/n)"), lang))
    {
        AnsiConsole.WriteLine();
        var initResult = InitHelper.InitializeWorkspace(botPath, selectedLanguage);
        if (initResult != 0)
        {
            AnsiConsole.MarkupLine($"\n[red]{lang.GetString("初始化失败。", "Initialization failed.")}[/]");
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]{lang.GetString("按任意键退出...", "Press any key to exit...")}[/]");
        Console.ReadKey(true);
        Environment.Exit(1);
        return;
    }

    AnsiConsole.MarkupLine($"\n[grey]{lang.GetString("初始化已取消。如需手动初始化，请运行 /init 命令", "Initialization cancelled. Run /init command to initialize manually")}[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[grey]{lang.GetString("按任意键退出...", "Press any key to exit...")}[/]");
    Console.ReadKey(true);
    Environment.Exit(0);
    return;
}

var configPath = Path.Combine(botPath, "appsettings.json");
var config = AppConfig.LoadWithGlobalFallback(configPath);

// Create language service from config
var languageService = new LanguageService(config.Language);

DebugModeService.Initialize(config.DebugMode);
if (config.DebugMode)
{
    AnsiConsole.MarkupLine("[yellow]Debug mode is enabled - tool arguments and results will be shown in full[/]");
}

if (string.IsNullOrWhiteSpace(config.ApiKey))
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[yellow]⚠️  {languageService.GetString("API Key 未配置", "API Key not configured")}[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine(languageService.GetString(
        "请在配置文件中设置 API Key 后再运行：",
        "Please set API Key in configuration file before running:"));
    AnsiConsole.WriteLine($"  {configPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine(languageService.GetString("配置示例：", "Configuration example:"));
    AnsiConsole.WriteLine("  \"ApiKey\": \"your-api-key-here\"");
    AnsiConsole.WriteLine("  \"Model\": \"gpt-4o-mini\"");
    AnsiConsole.WriteLine("  \"EndPoint\": \"https://api.openai.com/v1\"");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[grey]{languageService.GetString("按任意键退出...", "Press any key to exit...")}[/]");
    Console.ReadKey(true);
    Environment.Exit(1);
    return;
}

await using var provider = new ServiceCollection()
    .AddDotBot(config, workspacePath, botPath)
    .AddSingleton<IDotBotHost>(config switch
    {
        { QQBot.Enabled: true } => sp => ActivatorUtilities.CreateInstance<QQBotHost>(sp),
        { WeComBot.Enabled: true } => sp => ActivatorUtilities.CreateInstance<WeComBotHost>(sp),
        { Api.Enabled: true } => sp => ActivatorUtilities.CreateInstance<ApiHost>(sp),
        _ => sp => ActivatorUtilities.CreateInstance<CliHost>(sp)
    })
    .BuildServiceProvider();

await provider.InitializeServicesAsync();

try
{
    var host = provider.GetRequiredService<IDotBotHost>();
    await using (host)
    {
        await host.RunAsync();
    }
}
finally
{
    await provider.DisposeServicesAsync();
}

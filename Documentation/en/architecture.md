# DotBot Architecture & Security

DotBot is a general-purpose, tool-driven intelligent agent system with a modular architecture design. It runs in four modes: local REPL, QQ Bot, WeCom Bot, and API Service. Its design goal is "controllable, extensible, and auditable", constraining high-risk operations through security layers and approval mechanisms.

- Use cases: Development collaboration assistant, controlled automation (files, Shell, Web), team QQ/WeCom assistant, API service integration
- Runtime modes: CLI (REPL), QQ (OneBot V11 reverse WebSocket), WeCom Bot (WeCom HTTP callback), API (OpenAI-compatible HTTP service)
- Extension methods: Tools & Skills (place SKILL.md text skills under workspace `skills/`), MCP external tool services

---

## Architecture Overview

```
DotBot/
├─ Program.cs                 # Entry: validate workspace, load AppConfig, HostBuilder selects module
├─ AppConfig.cs               # Layered config (global ~/.bot + workspace .bot/appsettings.json)
│
├─ Abstractions/              # Core abstraction interfaces
│  ├─ IDotBotModule.cs        # Module interface: defines channel/mode abstraction
│  ├─ IHostFactory.cs         # Host factory interface
│  ├─ IAgentToolProvider.cs   # Tool provider interface
│  ├─ IApprovalServiceFactory.cs # Approval service factory interface
│  ├─ ModuleContext.cs        # Module context
│  ├─ ToolProviderContext.cs  # Tool provider context
│  ├─ ApprovalServiceContext.cs # Approval service context
│  └─ ModuleBase.cs           # Module base class
│
├─ Modules/                   # Module definitions
│  ├─ Registry/
│  │  └─ ModuleRegistry.cs    # Module registry: discovery, registration, selection
│  ├─ CliModule.cs            # CLI module
│  ├─ ApiModule.cs            # API module
│  ├─ QQModule.cs             # QQ module
│  └─ WeComModule.cs          # WeCom module
│
├─ Hosting/                   # Host related
│  └─ HostBuilder.cs          # Launcher: coordinates module selection & service config
│
├─ Commands/                  # Command system
│  ├─ Core/
│  │  ├─ ICommandHandler.cs   # Command handler interface
│  │  ├─ ICommandResponder.cs # Command responder interface
│  │  ├─ CommandDispatcher.cs # Command dispatcher
│  │  ├─ CommandContext.cs    # Command context
│  │  └─ CommandResult.cs     # Command execution result
│  ├─ Handlers/
│  │  ├─ NewCommandHandler.cs
│  │  ├─ DebugCommandHandler.cs
│  │  ├─ HelpCommandHandler.cs
│  │  ├─ HeartbeatCommandHandler.cs
│  │  └─ CronCommandHandler.cs
│  └─ ChannelAdapters/
│     ├─ QQCommandResponder.cs
│     └─ WeComCommandResponder.cs
│
├─ Configuration/             # Configuration mapping system
│  ├─ Contracts/
│  │  ├─ IModuleConfigBinder.cs   # Module config binder interface
│  │  └─ IModuleConfigProvider.cs # Module config provider interface
│  ├─ Core/
│  │  └─ ModuleConfigProvider.cs  # Module config provider implementation
│  ├─ Modules/
│  │  ├─ QQModuleConfig.cs
│  │  ├─ WeComModuleConfig.cs
│  │  ├─ ApiModuleConfig.cs
│  │  └─ CliModuleConfig.cs
│  └─ Binders/
│     └─ ...                    # Module config binders
│
├─ Hosting/
│  ├─ IDotBotHost.cs       # Host interface: RunAsync + IAsyncDisposable
│  ├─ ServiceRegistration.cs  # DI service registration extension (AddDotBot)
│  ├─ DotBotPaths.cs       # Path configuration (WorkspacePath/DotBotPath)
│  ├─ CliHost.cs              # CLI (REPL) mode Host
│  ├─ QQBotHost.cs            # QQ Bot mode Host
│  ├─ WeComBotHost.cs         # WeCom Bot mode Host
│  ├─ ApiHost.cs              # API mode Host (OpenAI-compatible)
│  └─ ApiApprovalService.cs   # API mode approval service
│
├─ Agents/
│  ├─ AgentFactory.cs         # Build AIAgent: aggregate IAgentToolProvider, inject Memory/Skills
│  └─ SubAgentManager.cs      # SubAgent (restricted tools)
│
├─ Tools/
│  ├─ Providers/              # Tool providers (layered)
│  │  ├─ Core/
│  │  │  └─ CoreToolProvider.cs    # Core tools: File/Shell/Web/Agent
│  │  ├─ Channels/
│  │  │  ├─ QQToolProvider.cs      # QQ channel tools
│  │  │  └─ WeComToolProvider.cs   # WeCom channel tools
│  │  └─ System/
│  │     ├─ CronToolProvider.cs    # Scheduled task tools
│  │     └─ McpToolProvider.cs     # MCP tools
│  ├─ AgentTools.cs           # SpawnSubagent
│  ├─ FileTools.cs            # File read/write/edit/list
│  ├─ ShellTools.cs           # Shell execution
│  ├─ WebTools.cs             # Web search & scraping
│  └─ WeComTools.cs           # WeCom notifications
│
├─ Mcp/
│  ├─ McpServerConfig.cs      # MCP server config model
│  └─ McpClientManager.cs     # MCP client lifecycle management
│
├─ Heartbeat/
│  └─ HeartbeatService.cs     # Heartbeat service
│
├─ Cron/
│  ├─ CronService.cs          # Scheduled task scheduling
│  ├─ CronTypes.cs            # Data models
│  └─ CronTools.cs            # Agent tools
│
├─ Security/
│  ├─ IApprovalService.cs     # Approval interface
│  ├─ ConsoleApprovalService  # CLI approval interaction
│  ├─ PathBlacklist.cs        # Path blacklist
│  ├─ ShellCommandInspector   # Shell command path static analysis
│  ├─ ApprovalContextScope.cs # AsyncLocal approval context
│  └─ ApprovalStore.cs        # Approval decision persistence
│
├─ Memory/
│  ├─ MemoryStore.cs          # Memory system
│  └─ SessionStore.cs         # Session persistence
│
├─ Context/
│  ├─ MemoryContextProvider   # AIContextProvider
│  └─ PromptBuilder.cs        # Assemble system prompt
│
├─ QQ/
│  ├─ Factories/
│  │  ├─ QQClientFactory.cs       # QQ client factory
│  │  └─ QQApprovalServiceFactory.cs # QQ approval service factory
│  ├─ OneBotReverseWsServer   # Reverse WS server
│  ├─ QQBotClient.cs          # Event/action encapsulation
│  ├─ QQChannelAdapter.cs     # Message handling
│  ├─ QQApprovalService.cs    # QQ approval interaction
│  └─ QQPermissionService.cs  # Permission tiers
│
├─ WeCom/
│  ├─ Factories/
│  │  ├─ WeComClientFactory.cs    # WeCom client factory
│  │  └─ WeComApprovalServiceFactory.cs # WeCom approval service factory
│  ├─ WeComBotServer.cs       # HTTP server
│  ├─ WeComBotRegistry.cs     # Multi-bot routing
│  ├─ WeComChannelAdapter.cs  # Message handling
│  ├─ WeComApprovalService.cs # Approval interaction
│  └─ WeComPermissionService.cs # Permission tiers
│
├─ Api/
│  └─ Factories/
│     └─ ApiApprovalServiceFactory.cs # API approval service factory
│
├─ CLI/
│  ├─ Factories/
│  │  └─ ConsoleApprovalServiceFactory.cs # Console approval service factory
│  ├─ ReplHost.cs             # REPL loop
│  ├─ InitHelper.cs           # Workspace initialization
│  └─ Rendering/*             # Event rendering
│
└─ Skills/
   └─ SkillsLoader.cs         # Skills loader
```

---

## Modular Architecture

### Core Concepts

DotBot uses a modular architecture where each runtime mode (CLI, API, QQ, WeCom) is an independent module:

```
┌─────────────────────────────────────────────────────────────────┐
│                         Program.cs                              │
│                   (Entry + Config Loading)                      │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                        HostBuilder                              │
│                     (Bot Launcher)                              │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                      ModuleRegistry                             │
│       (Module Discovery + Registration + Selection)             │
│                                                                 │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐          │
│   │CliModule│  │ApiModule│  │QQModule │  │WeComMod │          │
│   │Priority:0│  │Priority:10│ │Priority:20│ │Priority:30│        │
│   └─────────┘  └─────────┘  └─────────┘  └─────────┘          │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                       IHostFactory                              │
│                  (Create IDotBotHost)                          │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                       IDotBotHost                               │
│         (CliHost / ApiHost / QQBotHost / WeComBotHost)         │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                    IAgentToolProvider                           │
│           (CoreToolProvider + Channel Tools)                   │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                      AgentFactory                               │
│           (Aggregate Tools + Build AIAgent)                    │
└─────────────────────────────────────────────────────────────────┘
```

### Module Interface

```csharp
public interface IDotBotModule
{
    string Name { get; }           // Module name (cli, api, qq, wecom)
    int Priority => 0;             // Priority (higher priority modules selected first)
    bool IsEnabled(AppConfig config); // Check if enabled based on config
    void ConfigureServices(IServiceCollection services, ModuleContext context);
}
```

### Module Priorities

| Module | Priority | Enable Condition |
|--------|----------|------------------|
| CLI | 0 | When no other modules enabled (default fallback) |
| API | 10 | `Api.Enabled = true` |
| WeCom | 20 | `WeComBot.Enabled = true` |
| QQ | 30 | `QQBot.Enabled = true` |

### Module Discovery

Module registration supports two discovery methods:

1. **Source Generator** (preferred): Mark classes with `DotBotModuleAttribute` and `HostFactoryAttribute`, auto-generate registration code at compile time
2. **Reflection** (fallback): Scan assembly at runtime for types implementing `IDotBotModule` and `IHostFactory`

```csharp
// Module attribute example
[DotBotModule("cli", Priority = 0, Description = "CLI module")]
public sealed class CliModule : ModuleBase { ... }

[HostFactory("cli")]
public sealed class CliHostFactory : IHostFactory { ... }
```

---

## Command System

### Architecture

The command system uses a unified processing model, eliminating duplicate command handling logic across channels:

```
┌─────────────────────────────────────────────────────────────────┐
│                    CommandDispatcher                            │
│                     (Command Dispatcher)                        │
└──────────────────────────────┬──────────────────────────────────┘
                               │
         ┌─────────────────────┼─────────────────────┐
         │                     │                     │
         ▼                     ▼                     ▼
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│NewCmdHandler│      │HelpCmdHandler│     │DebugCmdHandler│
│  Commands:  │      │  Commands:  │      │  Commands:  │
│  ["/new"]   │      │ ["/help"]   │      │ ["/debug"]  │
└─────────────┘      └─────────────┘      └─────────────┘
         │                     │                     │
         └─────────────────────┼─────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                    ICommandResponder                            │
│          (QQCommandResponder / WeComCommandResponder)           │
└─────────────────────────────────────────────────────────────────┘
```

### Core Interfaces

```csharp
// Command handler
public interface ICommandHandler
{
    string[] Commands { get; }  // Supported commands (e.g., "/new", "/clear")
    Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder);
}

// Command responder (channel adapter)
public interface ICommandResponder
{
    Task SendTextAsync(string text);
    Task SendMarkdownAsync(string markdown);
}
```

### Built-in Commands

| Command | Handler | Description |
|---------|---------|-------------|
| `/new` | NewCommandHandler | Start new session |
| `/help` | HelpCommandHandler | Show help information |
| `/debug` | DebugCommandHandler | Toggle debug mode |
| `/heartbeat` | HeartbeatCommandHandler | Trigger heartbeat check |
| `/cron` | CronCommandHandler | Scheduled task management |

---

## Tool Provider System

### Layered Design

Tools are registered in layers by responsibility, supporting flexible composition:

```
┌─────────────────────────────────────────────────────────────────┐
│                    AgentFactory                                 │
│              (Tool Aggregation + Agent Build)                   │
└──────────────────────────────┬──────────────────────────────────┘
                               │
         ┌─────────────────────┼─────────────────────┐
         │                     │                     │
         ▼                     ▼                     ▼
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│CoreToolProvider│    │ChannelToolProviders│ │SystemToolProviders│
│ Priority: 10 │      │ Priority: 50+ │     │ Priority: 100+ │
├─────────────┤      ├─────────────┤      ├─────────────┤
│ - AgentTools│      │ - QQTools   │      │ - CronTools │
│ - FileTools │      │ - WeComTools│      │ - McpTools  │
│ - ShellTools│      │             │      │             │
│ - WebTools  │      │             │      │             │
└─────────────┘      └─────────────┘      └─────────────┘
```

### Tool Provider Interface

```csharp
public interface IAgentToolProvider
{
    int Priority => 100;  // Priority (lower numbers registered first)
    IEnumerable<AITool> CreateTools(ToolProviderContext context);
}
```

### Tool Priorities

| Provider | Priority | Description |
|----------|----------|-------------|
| CoreToolProvider | 10 | Core tools (File/Shell/Web/Agent) |
| WeComToolProvider | 50 | WeCom notification tools |
| QQToolProvider | 50 | QQ-related tools |
| CronToolProvider | 100 | Scheduled task tools |
| McpToolProvider | 100 | MCP tools |

---

## Configuration Mapping System

### Module Config Binding

Each module can have its own configuration model and binder:

```csharp
public interface IModuleConfigBinder<TConfig>
{
    string SectionName { get; }  // Config section name
    TConfig Bind(AppConfig appConfig);  // Bind from AppConfig
    IReadOnlyList<string> Validate(TConfig config);  // Validate config
}
```

### Config Models

```
AppConfig (Main Config)
    ├── QQModuleConfig (QQBot.* mapping)
    ├── WeComModuleConfig (WeComBot.* mapping)
    ├── ApiModuleConfig (Api.* mapping)
    └── CliModuleConfig (CLI-related config)
```

---

## Key Paths

1. **Startup**: `Program.cs` -> Detect workspace -> `AppConfig.LoadWithGlobalFallback`
2. **Module Discovery**: `ModuleRegistry` -> Source generator or reflection discovers modules
3. **Module Selection**: `HostBuilder.Build()` -> Select enabled module by priority
4. **Service Configuration**: `module.ConfigureServices()` -> Register module-specific services
5. **Host Creation**: `IHostFactory.CreateHost()` -> Create `IDotBotHost` instance
6. **Agent Construction**: `AgentFactory` -> Aggregate `IAgentToolProvider` -> Register tools
7. **Run**: `host.RunAsync()` -> Enter channel event loop

---

## Configuration System

DotBot uses layered configuration where workspace config overrides global config:

| Config Item | Default | Description |
|-------------|---------|-------------|
| `ApiKey` | — | OpenAI-compatible API Key (required) |
| `Model` | `gpt-4o-mini` | Model to use |
| `EndPoint` | `https://api.openai.com/v1` | API endpoint |
| `SystemInstructions` | Built-in Chinese prompt | System prompt |
| `MaxToolCallRounds` | 30 | Main Agent max tool call rounds |
| `SubagentMaxToolCallRounds` | 15 | SubAgent max tool call rounds |
| `Tools.File.RequireApprovalOutsideWorkspace` | true | Require approval for file access outside workspace |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | true | Require approval for commands outside workspace |
| `Tools.Shell.Timeout` | 60 | Shell command timeout (seconds) |
| `Security.BlacklistedPaths` | [] | Path blacklist |
| `QQBot.Enabled` | false | Enable QQ Bot mode |
| `WeCom.Enabled` | false | Enable WeCom notifications |
| `WeCom.WebhookUrl` | — | WeCom group bot Webhook URL |

For detailed configuration, see [Configuration Guide](./config_guide.md).

---

## Extension Guide

### Adding a New Module

1. **Create Module Class**:

```csharp
[DotBotModule("mymodule", Priority = 40, Description = "My Module")]
public sealed class MyModule : ModuleBase
{
    public override string Name => "mymodule";
    public override int Priority => 40;
    
    public override bool IsEnabled(AppConfig config)
    {
        return config.MyModule?.Enabled ?? false;
    }
    
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // Register module-specific services
        services.AddSingleton<IMyService, MyService>();
    }
}
```

2. **Create Host Factory**:

```csharp
[HostFactory("mymodule")]
public sealed class MyHostFactory : IHostFactory
{
    public string ModeName => "mymodule";
    
    public IDotBotHost CreateHost(IServiceProvider provider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<MyHost>(provider);
    }
}
```

3. **Create Host Implementation**:

```csharp
public sealed class MyHost : IDotBotHost
{
    public async Task RunAsync() { /* Implement run logic */ }
    public ValueTask DisposeAsync() { /* Cleanup resources */ }
}
```

### Adding a New Tool Provider

```csharp
public sealed class MyToolProvider : IAgentToolProvider
{
    public int Priority => 60;  // After core tools, before system tools
    
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var myTools = new MyTools(context.Config);
        yield return AIFunctionFactory.Create(myTools.MyFunction);
    }
}
```

### Adding a New Command Handler

```csharp
public sealed class MyCommandHandler : ICommandHandler
{
    public string[] Commands => ["/mycommand"];
    
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        await responder.SendTextAsync("Executing my command");
        return CommandResult.Handled;
    }
}
```

---

## Memory & Sessions

- **MemoryStore** (`Memory/MemoryStore.cs`):
  - Daily notes: `<.bot>/memory/YYYY-MM-DD.md`
  - Long-term memory: `<.bot>/memory/MEMORY.md`
  - Context injection: Combined into SystemPrompt before each turn

- **SessionStore** (`Memory/SessionStore.cs`):
  - Serialization based on `Microsoft.Agents.AI.AgentSession`
  - Session files: `<.bot>/sessions/<id>.json`
  - Supports session compaction (removes tool messages, reduces storage)

---

## Security Model

### 1) Path Blacklist

- FileTools: Direct reject for blacklisted paths
- ShellTools: Reject when command strings reference blacklisted paths

### 2) Workspace Boundary

- **FileTools**: Access outside workspace -> configured to "reject" or "initiate approval"
- **ShellTools**: Static analysis of command strings, identifies absolute paths, home directory, HOME variable

### 3) Dangerous Commands

- Deny patterns: `rm -rf`, `mkfs`, `dd`, `shutdown`, fork bomb, etc.
- Timeout (default 60s) and output limit (default 10000 chars)

### 4) Approval Flow

- **CLI**: Always/Session/Once/Reject, decisions persisted
- **QQ**: In-session approval, @mention, keywords "approve/reject"
- **WeCom**: In-session approval, keywords "approve/approve all/reject"

### 5) Roles & Operation Tiers

- Roles: `Admin` / `Whitelisted` / `Unauthorized`
- Operation levels: Tier 0 (Chat) -> Tier 1 (ReadOnly) -> Tier 2 (WriteWorkspace) -> Tier 3 (WriteOutsideWorkspace)

See [QQ Bot Guide](./qq_bot_guide.md) and [WeCom Guide](./wecom_guide.md).

---

## Runtime Modes

### CLI (REPL)

- Single-user local development and debugging
- Approval via console (Always/Session/Once/Reject)
- Session management: `/session`, `/memory`, `/skills` commands

### QQ Bot

- Group chat/private chat entry, NapCat reverse WebSocket connection
- Permission tiers: Based on QQ number + group number config
- Session isolation: Independent session per QQ number

### WeCom Bot

- Group chat/single chat entry, HTTP server receives message callbacks
- Permission tiers: Based on WeCom UserId + ChatId config
- Session isolation: Independent per user+session

### API (OpenAI-compatible Service)

- Based on Microsoft.Agents.AI.Hosting.OpenAI framework
- Endpoint: `POST /dotbot/v1/chat/completions`
- Bearer Token authentication (optional)
- Tool filtering: Via `EnabledTools` config

---

## Skills (Text Skills)

- Location: `<workspace>/skills/<name>/SKILL.md` or `~/.bot/skills/<name>/SKILL.md`
- Frontmatter metadata: `always`, `bins`, `env`
- Loading logic: Workspace takes priority, `always=true` auto-injected

---

## Capability Boundaries & Recommendations

- **Files**: Read/write within workspace; blacklist priority; single file limit 10MB
- **Shell**: Dangerous commands blocked; out-of-workspace detection & approval; timeout 60s
- **Web**: Search returns 5 results by default; scrape limit 50000 chars
- **SubAgent**: Restricted tools; direct reject outside workspace; iteration limit 15 rounds
- **Heartbeat**: Periodically read HEARTBEAT.md and execute
- **Cron**: Scheduled task scheduling (at/every), JSON persistence
- **MCP**: Connect external tool servers

---

## Source Code Reference

All paths relative to `DotBot/`:

- Entry & config: `Program.cs`, `AppConfig.cs`
- Abstractions: `Abstractions/IDotBotModule.cs`, `Abstractions/IHostFactory.cs`, `Abstractions/IAgentToolProvider.cs`
- Module system: `Modules/Registry/ModuleRegistry.cs`, `Modules/CliModule.cs`, `Modules/QQModule.cs`, `Modules/WeComModule.cs`, `Modules/ApiModule.cs`
- Launcher: `Hosting/HostBuilder.cs`
- Command system: `Commands/Core/CommandDispatcher.cs`, `Commands/Core/ICommandHandler.cs`, `Commands/Handlers/*.cs`
- Config mapping: `Configuration/Contracts/IModuleConfigBinder.cs`, `Configuration/Core/ModuleConfigProvider.cs`
- Host architecture: `Hosting/IDotBotHost.cs`, `Hosting/ServiceRegistration.cs`, `Hosting/CliHost.cs`, `Hosting/QQBotHost.cs`, `Hosting/WeComBotHost.cs`, `Hosting/ApiHost.cs`
- Agent construction: `Agents/AgentFactory.cs`, `Agents/SubAgentManager.cs`
- Tool providers: `Tools/Providers/Core/CoreToolProvider.cs`, `Tools/Providers/Channels/*.cs`, `Tools/Providers/System/*.cs`
- Tool implementations: `Tools/AgentTools.cs`, `Tools/FileTools.cs`, `Tools/ShellTools.cs`, `Tools/WebTools.cs`
- MCP: `Mcp/McpServerConfig.cs`, `Mcp/McpClientManager.cs`
- Heartbeat & cron: `Heartbeat/HeartbeatService.cs`, `Cron/CronService.cs`, `Cron/CronTools.cs`
- Security: `Security/IApprovalService.cs`, `Security/PathBlacklist.cs`, `Security/ShellCommandInspector.cs`
- QQ integration: `QQ/Factories/*.cs`, `QQ/QQBotClient.cs`, `QQ/QQChannelAdapter.cs`
- WeCom integration: `WeCom/Factories/*.cs`, `WeCom/WeComBotServer.cs`, `WeCom/WeComChannelAdapter.cs`
- Skills: `Skills/SkillsLoader.cs`

## Related Documentation

- [Configuration & Security Guide](./config_guide.md)
- [API Mode Guide](./api_guide.md)
- [QQ Bot Guide](./qq_bot_guide.md)
- [WeCom Guide](./wecom_guide.md)
- [Documentation Index](./index.md)

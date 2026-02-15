# DotBot Architecture & Security

DotBot is a general-purpose, tool-driven intelligent agent system that runs in four modes: local REPL, QQ Bot, WeCom Bot, and API Service. Its design goal is "controllable, extensible, and auditable", constraining high-risk operations through security layers and approval mechanisms.

- Use cases: Development collaboration assistant, controlled automation (files, Shell, Web), team QQ/WeCom assistant, API service integration
- Runtime modes: CLI (REPL), QQ (OneBot V11 reverse WebSocket), WeCom Bot (WeCom HTTP callback), API (OpenAI-compatible HTTP service)
- Extension methods: Tools & Skills (place SKILL.md text skills under workspace `skills/`), MCP external tool services

---

## Architecture Overview

```
DotBot/
├─ Program.cs                 # Entry: validate workspace, load AppConfig, select Host to run
├─ AppConfig.cs               # Layered config (global ~/.bot + workspace .bot/appsettings.json)
│
├─ Hosting/
│  ├─ IDotBotHost.cs       # Host interface: RunAsync + IAsyncDisposable
│  ├─ ServiceRegistration.cs  # DI service registration extension (AddDotBot)
│  ├─ DotBotPaths.cs       # Path configuration (WorkspacePath/DotBotPath)
│  ├─ CliHost.cs              # CLI (REPL) mode Host
│  ├─ QQBotHost.cs            # QQ Bot mode Host
│  ├─ WeComBotHost.cs         # WeCom Bot mode Host
│  ├─ ApiHost.cs              # API mode Host (OpenAI-compatible, based on Microsoft.Agents.AI.Hosting)
│  └─ ApiApprovalService.cs   # API mode approval service (auto approve/reject)
│
├─ Agents/
│  ├─ AgentFactory.cs         # Build AIAgent: integrate FunctionInvokingChatClient, register Tools, inject Memory/Skills
│  └─ SubAgentManager.cs      # SubAgent (restricted tools): workspace-only File/Shell/Web, iteration limit
│
├─ Tools/
│  ├─ AgentTools.cs           # SpawnSubagent: delegate subtasks to restricted SubAgent
│  ├─ FileTools.cs            # File read/write/edit/list (workspace boundary + blacklist + approval)
│  ├─ ShellTools.cs           # Shell execution (dangerous pattern interception, out-of-workspace path detection, approval, timeout/output limit)
│  ├─ WebTools.cs             # Web search (Bing/Exa/DuckDuckGo configurable) and Web scraping (char limit & timeout)
│  └─ WeComTools.cs           # WeCom group bot Webhook notification (text message, Agent tool + public method)
│
├─ Mcp/
│  ├─ McpServerConfig.cs      # MCP server config model
│  └─ McpClientManager.cs     # MCP client lifecycle management and tool collection
│
├─ Heartbeat/
│  └─ HeartbeatService.cs     # Heartbeat service: periodically read HEARTBEAT.md and submit to Agent
│
├─ Cron/
│  ├─ CronService.cs          # Scheduled task scheduling (at/every), JSON persistence
│  ├─ CronTypes.cs            # Data models (CronJob, CronSchedule, CronPayload, CronJobState)
│  └─ CronTools.cs            # Agent tools: self-service create/view/delete scheduled tasks
│
├─ Security/
│  ├─ IApprovalService.cs     # Approval interface: file/command (with ApprovalContext)
│  ├─ ConsoleApprovalService  # CLI approval interaction: Always/Session/Once/Reject
│  ├─ PathBlacklist.cs        # Path blacklist (includes command string reference detection)
│  ├─ ShellCommandInspector   # Shell command path static analysis (absolute path/~/HOME out-of-workspace detection)
│  ├─ ApprovalContextScope.cs # AsyncLocal approval context (user/group/source)
│  └─ ApprovalStore.cs        # Approval decision persistence (Always/Session level cache)
│
├─ Memory/
│  ├─ MemoryStore.cs          # Memory system (daily notes YYYY-MM-DD.md + long-term memory MEMORY.md)
│  └─ SessionStore.cs         # Session persistence (JSON serialization, supports Microsoft.Agents.AI AgentSession)
│
├─ Context/
│  ├─ MemoryContextProvider   # AIContextProvider: inject Memory + Skills + SystemPrompt per turn
│  └─ PromptBuilder.cs        # Assemble system prompt (base instructions + memory context + skills summary)
│
├─ QQ/
│  ├─ OneBotReverseWsServer   # Reverse WS server, NapCat client connects
│  ├─ QQBotClient.cs          # Event/action encapsulation
│  ├─ QQChannelAdapter.cs     # Message handling: permission check, session binding, Agent streaming response
│  ├─ QQApprovalService.cs    # QQ approval interaction (@mention + timeout)
│  ├─ QQPermissionService.cs  # Roles and operation tiers (Admin/Whitelisted/Unauthorized + Tier0~3)
│  └─ OneBot/*                # OneBot V11 data structures
│
├─ WeCom/
│  ├─ WeComBotServer.cs       # WeCom HTTP server (URL verification + message callback)
│  ├─ WeComBotRegistry.cs     # Multi-bot routing and encryption/decryption registry
│  ├─ WeComChannelAdapter.cs  # Message handling: permission check, session binding, Agent streaming response
│  ├─ WeComApprovalService.cs # WeCom approval interaction (in-chat interaction + timeout)
│  ├─ WeComPermissionService.cs # Roles and operation tiers (Admin/Whitelisted/Unauthorized + Tier)
│  ├─ WeComPusher.cs          # Message pusher (Text/Markdown/Image/News/Voice/File)
│  ├─ WeComPusherScope.cs     # AsyncLocal Pusher scope (for approval service to get current pusher)
│  ├─ WeComMessage.cs         # Message models (XML/JSON parsing)
│  └─ WeComBizMsgCrypt.cs     # AES-256-CBC + SHA1 encryption/decryption
│
├─ CLI/
│  ├─ ReplHost.cs             # Local REPL loop (tool streaming render/approval pause)
│  ├─ InitHelper.cs           # First-time workspace initialization wizard
│  └─ Rendering/*             # Event rendering (tool calls, response text, warnings/errors)
│
└─ Skills/
   └─ SkillsLoader.cs         # Load skills from <workspace>/skills and ~/.bot/skills (supports always=true, bins/env requirement check)
```

### Key Paths

1. **Startup**: `Program.cs` -> Detect workspace (`.bot/`, if missing then guide `InitHelper` initialization) -> `AppConfig.LoadWithGlobalFallback` (merge global `~/.bot/appsettings.json` + workspace config)
2. **Service Registration**: `ServiceCollection.AddDotBot()` -> DI container registers AppConfig / DotBotPaths / MemoryStore / SessionStore / ApprovalStore / PathBlacklist / SkillsLoader / CronService / CronTools / McpClientManager / TraceStore / TraceCollector
3. **Host Selection** (by priority):
   - QQ mode (`QQBot.Enabled`): `QQBotHost` -> Create `QQBotClient` + permission/approval services -> `QQChannelAdapter` -> Reverse WS listener
   - WeCom mode (`WeComBot.Enabled`): `WeComBotHost` -> Create permission/approval services -> `WeComChannelAdapter` -> HTTP server listener
   - API mode (`Api.Enabled`): `ApiHost` -> `AgentFactory` -> `builder.AddAIAgent()` + `MapOpenAIChatCompletions()` -> HTTP service
   - CLI mode (default): `CliHost` -> Create `ConsoleApprovalService` -> `AgentFactory.CreateDefaultAgent()` -> `ReplHost.RunAsync()`
4. **Agent Construction (AgentFactory)**:
   - OpenAI-compatible `ChatClient` (supports any OpenAI protocol endpoint)
   - `FunctionInvokingChatClient`: Auto tool call chaining, limit `MaxToolCallRounds` (default 30)
   - Tool registration: `AgentTools.SpawnSubagent`, `FileTools.ReadFile/WriteFile/EditFile/ListDirectory`, `ShellTools.Exec`, `WebTools.WebSearch/WebFetch`, conditionally register `WeComTools.WeComNotify` (exposed via `AIFunctionFactory.Create`), MCP server tools (auto-injected via `McpClientTool`)
   - Context: `MemoryContextProvider` -> Assemble SystemPrompt + Memory + Skills Summary per turn via `PromptBuilder`
5. **SubAgent (SubAgentManager)**: Created with restricted config (`requireApprovalOutsideWorkspace: false` -> direct reject outside workspace), independent iteration limit (default 15 rounds), no EditFile or SpawnSubagent permissions
6. **WeCom Notifications (WeComTools)**: Conditionally registered (`WeCom.Enabled = true` + `WebhookUrl` non-empty), both as Agent tool (`WeComNotify`) and directly callable by Heartbeat/Cron (`SendTextAsync`)

---

## Configuration System

DotBot uses layered configuration where workspace config overrides global config:

| Config Item | Default | Description |
|-------------|---------|-------------|
| `ApiKey` | — | OpenAI-compatible API Key (required) |
| `Model` | `gpt-4o-mini` | Model to use |
| `EndPoint` | `https://api.openai.com/v1` | API endpoint (can switch to Ollama or other compatible endpoints) |
| `SystemInstructions` | Built-in Chinese prompt | System prompt |
| `MaxToolCallRounds` | 30 | Main Agent max tool call rounds |
| `SubagentMaxToolCallRounds` | 15 | SubAgent max tool call rounds |
| `Tools.File.RequireApprovalOutsideWorkspace` | true | Require approval for file access outside workspace (false=direct reject) |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | true | Require approval for commands outside workspace (false=direct reject) |
| `Tools.Shell.Timeout` | 60 | Shell command timeout (seconds) |
| `Security.BlacklistedPaths` | [] | Path blacklist |
| `QQBot.Enabled` | false | Enable QQ Bot mode |
| `WeCom.Enabled` | false | Enable WeCom notifications |
| `WeCom.WebhookUrl` | — | WeCom group bot Webhook URL |

For detailed configuration, see [Configuration Guide](./config_guide.md).

---

## Memory & Sessions

- **MemoryStore** (`Memory/MemoryStore.cs`):
  - Daily notes: `<.bot>/memory/YYYY-MM-DD.md`, Agent can append at any time
  - Long-term memory: `<.bot>/memory/MEMORY.md`, for cross-day knowledge persistence
  - Context injection: `GetMemoryContext()` combines long-term memory + today's notes into SystemPrompt before each turn

- **SessionStore** (`Memory/SessionStore.cs`):
  - Serialization/deserialization based on `Microsoft.Agents.AI.AgentSession`
  - Session files stored at `<.bot>/sessions/<id>.json`
  - Supports multi-session listing, switching, deletion (CLI via `/session` command)
  - Supports session compaction (`CompactSessions`, enabled by default): Automatically removes tool messages and FunctionCallContent when saving, reducing storage size

---

## Security Model

### 1) Path Blacklist (Security.BlacklistedPaths)

- FileTools: Direct reject for blacklisted paths and their sub-paths
- ShellTools: Direct reject when command strings reference blacklisted paths (via `PathBlacklist.CommandReferencesBlacklistedPath`)

### 2) Workspace Boundary (RequireApprovalOutsideWorkspace)

- **FileTools**: Access outside workspace -> configured to "reject" or "initiate approval"
- **ShellTools**: Not just checking cwd, but also statically analyzing command strings via `ShellCommandInspector`:
  - Identifies absolute paths (`/etc/...`), home directory (`~/.ssh/...`), HOME variable (`$HOME/...`, `${HOME}/...`)
  - Match triggers out-of-workspace treatment -> reject/approve based on config
  - Device path whitelist (`/dev/null`, `/dev/stdout`, etc.) is excluded from boundary checks

### 3) Dangerous Commands & System Protection

- Deny patterns: `rm -rf`, `mkfs`, `dd`, `shutdown`, fork bomb, etc.
- Timeout (default 60s) and output limit (default 10000 chars): Prevent long-running/oversized output

### 4) Approval Flow (IApprovalService)

- **Console (ConsoleApprovalService)**: Always/Session/Once/Reject, decisions can be persisted (`ApprovalStore`)
- **QQ (QQApprovalService)**: Initiate approval request in-session (group chat will @mention), keywords "approve/reject", timeout auto-reject
- **WeCom (WeComApprovalService)**: Initiate approval request in WeCom session, keywords "approve/approve all/reject", timeout auto-reject

### 5) Roles & Operation Tiers

#### QQ Mode (QQPermissionService)

- Roles: `Admin` / `Whitelisted` / `Unauthorized`
- Operation levels:
  - **Tier 0 (Chat)**: Conversation - all non-Unauthorized users
  - **Tier 1 (ReadOnly)**: Read files/Web - all non-Unauthorized users
  - **Tier 2 (WriteWorkspace)**: Write within workspace/commands - Admin only + approval
  - **Tier 3 (WriteOutsideWorkspace)**: Outside workspace - rejected for all roles by default

See [QQ Bot Guide](./qq_bot_guide.md).

#### WeCom Mode (WeComPermissionService)

- Roles: `Admin` / `Whitelisted` / `Unauthorized` (based on WeCom UserId)
- Operation levels: Same as QQ mode (Tier 0-3)
- Config items: `WeComBot.AdminUsers`, `WeComBot.WhitelistedUsers`, `WeComBot.WhitelistedChats`
- Approval timeout: `WeComBot.ApprovalTimeoutSeconds` (default 60 seconds)

See [WeCom Guide](./wecom_guide.md).

---

## Runtime Modes

### CLI (REPL)

- Single-user local development and debugging
- Approval via console (Always/Session/Once/Reject), supports render pause and resume
- Session management: `/session`, `/memory`, `/skills` and other built-in commands
- First-run automatic workspace initialization wizard (`InitHelper`)

### QQ Bot

- Group chat/private chat entry, NapCat reverse WebSocket connection
- Permission tiers: Admin/Whitelisted based on QQ number + group number config
- Session isolation: Independent session per QQ number (`QQChannelAdapter` manages)
- Approval loop: Initiate, wait, and process approvals within the same session

### WeCom Bot

- Group chat/single chat entry, HTTP server receives WeCom message callbacks
- Permission tiers: Admin/Whitelisted based on WeCom UserId + ChatId config
- Session isolation: Independent per user+session (`wecom_{chatId}_{userId}`)
- Approval loop: Initiate, wait, and process approvals within the same session

### API (OpenAI-compatible Service)

- Based on Microsoft.Agents.AI.Hosting.OpenAI framework, exposes OpenAI Chat Completions API
- Endpoint: `POST /dotbot/v1/chat/completions`, supports streaming and non-streaming
- External applications use standard OpenAI SDK (Python/JS/.NET etc.) to call, no custom SDK needed
- Bearer Token authentication (optional)
- Tool filtering: Selectively expose tools via `EnabledTools` config
- Operation approval: Supports AutoApprove auto-approve or auto-reject
- See [API Mode Guide](./api_guide.md)

---

## Skills (Text Skills)

- Location: `<workspace>/skills/<name>/SKILL.md` (priority) or `~/.bot/skills/<name>/SKILL.md`
- Frontmatter metadata:
  - `always: true` - Auto-inject into every turn's context
  - `bins: xxx` - Check if executables are available (comma-separated)
  - `env: FOO` - Check if environment variables exist (comma-separated)
- Loading logic (`SkillsLoader`):
  - Workspace skills of the same name take priority over user-level
  - `always=true` skills are auto-injected into SystemPrompt via `MemoryContextProvider`
  - Other skills are listed as summaries; Agent can read full content via `ReadFile` on demand
- Typical use: Call external programs via Shell tool

---

## Capability Boundaries & Recommendations

- **Files**: Read/write only within workspace; blacklist takes priority; single file limit 10MB (configurable)
- **Shell**: Dangerous commands blocked by default; out-of-workspace path detection & approval; timeout 60s / output limit 10000 chars
- **Web**: Search returns 5 results by default (configurable 1-10); scrape limit 50000 chars / timeout 30s; avoid downloading large content
- **SubAgent**: Restricted tools (no EditFile, no SpawnSubagent), direct reject outside workspace, iteration limit 15 rounds
- **Heartbeat**: Periodically read HEARTBEAT.md, auto-submit to Agent when executable content exists; disabled by default, configurable interval
- **Cron**: Scheduled task scheduling (at/every), JSON persistence, Agent can self-service create tasks via tools; disabled by default
- **MCP**: Connect external tool servers via Model Context Protocol, tools auto-registered to Agent

---

## Source Code Reference

All paths relative to `DotBot/`:

- Entry & config: `Program.cs`, `AppConfig.cs`
- Host architecture: `Hosting/IDotBotHost.cs`, `Hosting/ServiceRegistration.cs`, `Hosting/DotBotPaths.cs`, `Hosting/CliHost.cs`, `Hosting/QQBotHost.cs`, `Hosting/WeComBotHost.cs`, `Hosting/ApiHost.cs`
- Agent construction: `Agents/AgentFactory.cs`, `Agents/SubAgentManager.cs`
- Context & memory: `Context/MemoryContextProvider.cs`, `Context/PromptBuilder.cs`, `Memory/MemoryStore.cs`, `Memory/SessionStore.cs`
- Tools: `Tools/AgentTools.cs`, `Tools/FileTools.cs`, `Tools/ShellTools.cs`, `Tools/WebTools.cs`, `Tools/WeComTools.cs`
- MCP: `Mcp/McpServerConfig.cs`, `Mcp/McpClientManager.cs`
- Heartbeat & cron: `Heartbeat/HeartbeatService.cs`, `Cron/CronService.cs`, `Cron/CronTypes.cs`, `Cron/CronTools.cs`
- Security: `Security/IApprovalService.cs`, `Security/PathBlacklist.cs`, `Security/ShellCommandInspector.cs`, `Security/ConsoleApprovalService.cs`, `Security/ApprovalStore.cs`
- QQ integration: `QQ/OneBotReverseWsServer.cs`, `QQ/QQBotClient.cs`, `QQ/QQChannelAdapter.cs`, `QQ/QQApprovalService.cs`, `QQ/QQPermissionService.cs`
- WeCom integration: `WeCom/WeComBotServer.cs`, `WeCom/WeComChannelAdapter.cs`, `WeCom/WeComApprovalService.cs`, `WeCom/WeComPermissionService.cs`, `WeCom/WeComPusher.cs`
- Skills: `Skills/SkillsLoader.cs`

## Related Documentation

- [Configuration & Security Guide](./config_guide.md)
- [API Mode Guide](./api_guide.md)
- [QQ Bot Guide](./qq_bot_guide.md)
- [WeCom Guide](./wecom_guide.md)
- [Documentation Index](./index.md)

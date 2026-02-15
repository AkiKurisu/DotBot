# DotBot 架构与安全

DotBot 是一个通用型、工具驱动的智能体系统，可运行在本地 REPL、QQ 机器人、企业微信机器人和 API 服务四种形态。其设计目标是"可控、可扩展、可审计"，通过安全层与审批机制约束高风险操作。

- 适用场景：开发协作助手、受控自动化执行（文件、Shell、Web）、团队 QQ/微信助手、API 服务集成
- 运行形态：CLI（REPL）、QQ（OneBot V11 反向 WebSocket）、WeCom Bot（企业微信 HTTP 回调）、API（OpenAI 兼容 HTTP 服务）
- 扩展方式：Tools 与 Skills（工作区 skills/ 下放置 SKILL.md 文本技能）、MCP 外部工具服务

---

## 架构总览

```
DotBot/
├─ Program.cs                 # 入口：校验工作区、加载 AppConfig、选择 Host 运行
├─ AppConfig.cs               # 分层配置（全局 ~/.bot + 工作区 .bot/appsettings.json）
│
├─ Hosting/
│  ├─ IDotBotHost.cs       # Host 接口：RunAsync + IAsyncDisposable
│  ├─ ServiceRegistration.cs  # DI 服务注册扩展方法（AddDotBot）
│  ├─ DotBotPaths.cs       # 路径配置（WorkspacePath/DotBotPath）
│  ├─ CliHost.cs              # CLI（REPL）模式 Host
│  ├─ QQBotHost.cs            # QQ 机器人模式 Host
│  ├─ WeComBotHost.cs         # 企业微信机器人模式 Host
│  ├─ ApiHost.cs              # API 模式 Host（OpenAI 兼容，基于 Microsoft.Agents.AI.Hosting）
│  └─ ApiApprovalService.cs   # API 模式审批服务（自动批准/拒绝）
│
├─ Agents/
│  ├─ AgentFactory.cs         # 构建 AIAgent：接入 FunctionInvokingChatClient，注册 Tools，注入 Memory/Skills
│  └─ SubAgentManager.cs      # 子智能体（工具受限）：工作区内 File/Shell/Web，迭代上限
│
├─ Tools/
│  ├─ AgentTools.cs           # SpawnSubagent：委派子任务给受限子智能体
│  ├─ FileTools.cs            # 文件读写/编辑/列表（工作区边界 + 黑名单 + 审批）
│  ├─ ShellTools.cs           # Shell 执行（危险模式拦截、越界路径静态检测、审批、超时/输出上限）
│  ├─ WebTools.cs             # Web 搜索（Bing/Exa/DuckDuckGo 可配置）与 Web 抓取（字符数与超时限制）
│  └─ WeComTools.cs           # 企业微信群机器人 Webhook 通知（text 消息，Agent 工具 + 公共方法）
│
├─ Mcp/
│  ├─ McpServerConfig.cs      # MCP 服务器配置模型
│  └─ McpClientManager.cs     # MCP 客户端生命周期管理与工具收集
│
├─ Heartbeat/
│  └─ HeartbeatService.cs     # 心跳服务：定时读取 HEARTBEAT.md 并交给 Agent 执行
│
├─ Cron/
│  ├─ CronService.cs          # 定时任务调度（at/every），JSON 持久化
│  ├─ CronTypes.cs            # 数据模型（CronJob, CronSchedule, CronPayload, CronJobState）
│  └─ CronTools.cs            # Agent 工具：自助创建/查看/删除定时任务
│
├─ Security/
│  ├─ IApprovalService.cs     # 审批接口：文件/命令（带 ApprovalContext）
│  ├─ ConsoleApprovalService  # CLI 审批交互：Always/Session/Once/Reject
│  ├─ PathBlacklist.cs        # 路径黑名单（含命令字符串引用检测）
│  ├─ ShellCommandInspector   # Shell 命令路径静态分析（绝对路径/~/HOME 越界检测）
│  ├─ ApprovalContextScope.cs # AsyncLocal 审批上下文（用户/群/来源）
│  └─ ApprovalStore.cs        # 审批决策持久化（Always/Session 级别缓存）
│
├─ Memory/
│  ├─ MemoryStore.cs          # 记忆系统（每日笔记 YYYY-MM-DD.md + 长期记忆 MEMORY.md）
│  └─ SessionStore.cs         # 会话持久化（JSON 序列化，支持 Microsoft.Agents.AI AgentSession）
│
├─ Context/
│  ├─ MemoryContextProvider   # AIContextProvider：每轮注入 Memory + Skills + SystemPrompt
│  └─ PromptBuilder.cs        # 组装系统提示词（base instructions + memory context + skills summary）
│
├─ QQ/
│  ├─ OneBotReverseWsServer   # 反向 WS 服务端,NapCat 客户端连接
│  ├─ QQBotClient.cs          # 事件/动作封装
│  ├─ QQChannelAdapter.cs     # 消息处理：权限鉴别、会话绑定、Agent 流式响应
│  ├─ QQApprovalService.cs    # QQ 审批交互（@提醒 + 超时）
│  ├─ QQPermissionService.cs  # 角色与操作分级（Admin/Whitelisted/Unauthorized + Tier0~3）
│  └─ OneBot/*                # OneBot V11 数据结构
│
├─ WeCom/
│  ├─ WeComBotServer.cs       # WeCom HTTP 服务端（URL 验证 + 消息回调）
│  ├─ WeComBotRegistry.cs     # 多机器人路由与加解密注册表
│  ├─ WeComChannelAdapter.cs  # 消息处理：权限鉴别、会话绑定、Agent 流式响应
│  ├─ WeComApprovalService.cs # WeCom 审批交互（聊天内交互 + 超时）
│  ├─ WeComPermissionService.cs # 角色与操作分级（Admin/Whitelisted/Unauthorized + Tier）
│  ├─ WeComPusher.cs          # 消息推送器（Text/Markdown/Image/News/Voice/File）
│  ├─ WeComPusherScope.cs     # AsyncLocal Pusher 作用域（用于审批服务获取当前推送器）
│  ├─ WeComMessage.cs         # 消息模型（XML/JSON 解析）
│  └─ WeComBizMsgCrypt.cs     # AES-256-CBC + SHA1 加解密
│
├─ CLI/
│  ├─ ReplHost.cs             # 本地 REPL 循环（工具流式渲染/审批暂停）
│  ├─ InitHelper.cs           # 首次工作区初始化向导
│  └─ Rendering/*             # 事件渲染（工具调用、响应文本、警告/错误）
│
└─ Skills/
   └─ SkillsLoader.cs         # 从 <workspace>/skills 与 ~/.bot/skills 加载技能（支持 always=true、bins/env 需求检查）
```

### 关键路径

1. **启动**：`Program.cs` → 检测工作区（`.bot/`，不存在则引导 `InitHelper` 初始化）→ `AppConfig.LoadWithGlobalFallback`（全局 `~/.bot/appsettings.json` + 工作区配置合并）
2. **服务注册**：`ServiceCollection.AddDotBot()` → DI 容器注册 AppConfig / DotBotPaths / MemoryStore / SessionStore / ApprovalStore / PathBlacklist / SkillsLoader / CronService / CronTools / McpClientManager / TraceStore / TraceCollector
3. **Host 选择**（按优先级）：
   - QQ 模式（`QQBot.Enabled`）：`QQBotHost` → 创建 `QQBotClient` + 权限/审批服务 → `QQChannelAdapter` → 反向 WS 监听
   - WeCom 模式（`WeComBot.Enabled`）：`WeComBotHost` → 创建权限/审批服务 → `WeComChannelAdapter` → HTTP 服务器监听
   - API 模式（`Api.Enabled`）：`ApiHost` → `AgentFactory` → `builder.AddAIAgent()` + `MapOpenAIChatCompletions()` → HTTP 服务
   - CLI 模式（默认）：`CliHost` → 创建 `ConsoleApprovalService` → `AgentFactory.CreateDefaultAgent()` → `ReplHost.RunAsync()`
4. **Agent 构建（AgentFactory）**：
   - OpenAI 兼容 `ChatClient`（支持任何 OpenAI 协议端点）
   - `FunctionInvokingChatClient`：自动工具调用链，上限 `MaxToolCallRounds`（默认 30）
   - 工具注册：`AgentTools.SpawnSubagent`、`FileTools.ReadFile/WriteFile/EditFile/ListDirectory`、`ShellTools.Exec`、`WebTools.WebSearch/WebFetch`、条件注册 `WeComTools.WeComNotify`（通过 `AIFunctionFactory.Create` 暴露）、MCP 服务器工具（通过 `McpClientTool` 自动注入）
   - 上下文：`MemoryContextProvider` → 每轮通过 `PromptBuilder` 组装 SystemPrompt + Memory + Skills Summary
5. **子智能体（SubAgentManager）**：以受限配置创建（`requireApprovalOutsideWorkspace: false` → 工作区外直接拒绝），独立迭代上限（默认 15 轮），无 EditFile 和 SpawnSubagent 权限
6. **企业微信通知（WeComTools）**：条件注册（`WeCom.Enabled = true` + `WebhookUrl` 非空），既作为 Agent 工具（`WeComNotify`），也可被 Heartbeat/Cron 直接调用（`SendTextAsync`）

---

## 配置系统

DotBot 采用分层配置，工作区配置覆盖全局配置：

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `ApiKey` | — | OpenAI 兼容 API Key（必填） |
| `Model` | `gpt-4o-mini` | 使用的模型 |
| `EndPoint` | `https://api.openai.com/v1` | API 端点（可切换为 Ollama/其他兼容端点） |
| `SystemInstructions` | 内置中文提示 | 系统提示词 |
| `MaxToolCallRounds` | 30 | 主 Agent 最大工具调用轮数 |
| `SubagentMaxToolCallRounds` | 15 | 子智能体最大工具调用轮数 |
| `Tools.File.RequireApprovalOutsideWorkspace` | true | 文件越界时走审批（false=直接拒绝） |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | true | 命令越界时走审批（false=直接拒绝） |
| `Tools.Shell.Timeout` | 60 | Shell 命令超时（秒） |
| `Security.BlacklistedPaths` | [] | 路径黑名单列表 |
| `QQBot.Enabled` | false | 是否启用 QQ 机器人模式 |
| `WeCom.Enabled` | false | 是否启用企业微信通知 |
| `WeCom.WebhookUrl` | — | 企业微信群机器人 Webhook URL |

详细配置说明见 [配置指南](../CONFIG_GUIDE.md)。

---

## 记忆与会话

- **MemoryStore**（`Memory/MemoryStore.cs`）：
  - 每日笔记：`<.bot>/memory/YYYY-MM-DD.md`，Agent 可随时追加
  - 长期记忆：`<.bot>/memory/MEMORY.md`，用于跨日持久化知识
  - 上下文注入：`GetMemoryContext()` 在每轮对话前组合长期记忆 + 当日笔记注入 SystemPrompt

- **SessionStore**（`Memory/SessionStore.cs`）：
  - 基于 `Microsoft.Agents.AI.AgentSession` 的序列化/反序列化
  - 会话文件存储于 `<.bot>/sessions/<id>.json`
  - 支持多会话列表、切换、删除（CLI 通过 `/session` 命令管理）
  - 支持会话压缩（`CompactSessions`，默认开启）：保存时自动移除 tool 消息和 FunctionCallContent，减少存储体积

---

## 安全模型

### 1) 路径黑名单（Security.BlacklistedPaths）

- FileTools：对黑名单路径与其子路径直接拒绝
- ShellTools：命令字符串引用到黑名单路径时直接拒绝（通过 `PathBlacklist.CommandReferencesBlacklistedPath`）

### 2) 工作区边界（RequireApprovalOutsideWorkspace）

- **FileTools**：工作区外访问 → 配置为"拒绝"或"发起审批"
- **ShellTools**：不只看 cwd，还会通过 `ShellCommandInspector` 静态分析命令字符串：
  - 识别绝对路径（`/etc/...`）、家目录（`~/.ssh/...`）、HOME 变量（`$HOME/...`、`${HOME}/...`）
  - 命中则视为访问工作区外 → 根据配置拒绝/审批
  - 设备路径白名单（`/dev/null`、`/dev/stdout` 等）不计入越界

### 3) 危险命令与系统保护

- deny patterns：`rm -rf`、`mkfs`、`dd`、`shutdown`、fork bomb 等
- 超时（默认 60s）与输出上限（默认 10000 字符）：避免长时间/超大输出

### 4) 审批流（IApprovalService）

- **Console（ConsoleApprovalService）**：Always/Session/Once/Reject，决策可持久化（`ApprovalStore`）
- **QQ（QQApprovalService）**：在会话内发起审批请求（群聊会 @ 提醒），关键字"同意/拒绝"，超时自动拒绝
- **WeCom（WeComApprovalService）**：在企业微信会话内发起审批请求，关键字"同意/同意全部/拒绝"，超时自动拒绝

### 5) 角色与操作分级

#### QQ 模式（QQPermissionService）

- 角色：`Admin` / `Whitelisted` / `Unauthorized`
- 操作级别：
  - **Tier 0（Chat）**：对话 — 非 Unauthorized 均可
  - **Tier 1（ReadOnly）**：读文件/Web — 非 Unauthorized 均可
  - **Tier 2（WriteWorkspace）**：工作区内写入/命令 — 仅 Admin + 审批
  - **Tier 3（WriteOutsideWorkspace）**：工作区外 — 默认拒绝所有角色

详见 [QQ 机器人指南](./qq_bot_guide.md)。

#### WeCom 模式（WeComPermissionService）

- 角色：`Admin` / `Whitelisted` / `Unauthorized`（基于企业微信 UserId）
- 操作级别：与 QQ 模式相同（Tier 0-3）
- 配置项：`WeComBot.AdminUsers`、`WeComBot.WhitelistedUsers`、`WeComBot.WhitelistedChats`
- 审批超时：`WeComBot.ApprovalTimeoutSeconds`（默认 60 秒）

详见 [企业微信指南](./wecom_guide.md)。

---

## 运行形态

### CLI（REPL）

- 单用户本地开发与调试
- 审批走控制台（Always/Session/Once/Reject），支持渲染暂停与恢复
- 会话管理：`/session`、`/memory`、`/skills` 等内置命令
- 首次运行自动引导工作区初始化（`InitHelper`）

### QQ Bot

- 群聊/私聊入口，NapCat 反向 WebSocket 连接
- 权限分级：基于 QQ 号 + 群号配置 Admin/Whitelisted
- 会话隔离：每个 QQ 号独立会话（`QQChannelAdapter` 管理）
- 审批回路：在同一会话内发起、等待并处理审批

### WeCom Bot

- 群聊/单聊入口，HTTP 服务器接收企业微信消息回调
- 权限分级：基于企业微信 UserId + ChatId 配置 Admin/Whitelisted
- 会话隔离：每个用户+会话独立（`wecom_{chatId}_{userId}`）
- 审批回路：在同一会话内发起、等待并处理审批

### API（OpenAI 兼容服务）

- 基于 Microsoft.Agents.AI.Hosting.OpenAI 框架，暴露 OpenAI Chat Completions API
- 端点：`POST /dotbot/v1/chat/completions`，支持 streaming 和非 streaming
- 外部应用使用标准 OpenAI SDK（Python/JS/.NET 等）即可调用，无需自定义 SDK
- Bearer Token 认证（可选）
- 工具过滤：通过 `EnabledTools` 配置选择性暴露工具
- 操作审批：支持 AutoApprove 自动批准或自动拒绝
- 详见 [API 模式指南](./api_guide.md)

---

## Skills（文本技能）

- 位置：`<workspace>/skills/<name>/SKILL.md`（优先）或 `~/.bot/skills/<name>/SKILL.md`
- 前置元数据（frontmatter）：
  - `always: true` — 自动注入到每轮上下文
  - `bins: xxx` — 检查可执行文件是否可用（逗号分隔）
  - `env: FOO` — 检查环境变量是否存在（逗号分隔）
- 加载逻辑（`SkillsLoader`）：
  - 工作区同名 Skill 优先于用户级
  - `always=true` 的 Skill 通过 `MemoryContextProvider` 自动注入 SystemPrompt
  - 其他 Skill 以摘要形式列出，Agent 可按需通过 `ReadFile` 读取完整内容
- 典型用法：通过 Shell 工具调用外部程序

---

## 能力边界与建议

- **文件**：仅工作区内读写；黑名单优先拦截；单文件上限 10MB（可配置）
- **Shell**：默认禁止危险命令；越界路径检测与审批；超时 60s / 输出上限 10000 字符
- **Web**：搜索默认返回 5 条结果（可配置 1-10）；抓取上限 50000 字符 / 超时 30s；避免下载大体量内容
- **子智能体**：工具受限（无 EditFile、无 SpawnSubagent）、工作区外直接拒绝、迭代上限 15 轮
- **Heartbeat**：定时读取 HEARTBEAT.md，有可执行内容时自动交给 Agent 处理；默认关闭，可配置间隔
- **Cron**：定时任务调度（at/every），JSON 持久化，Agent 可通过工具自助创建任务；默认关闭
- **MCP**：通过 Model Context Protocol 接入外部工具服务器，工具自动注册到 Agent

---

## 参考源码

所有路径相对于 `DotBot/`：

- 入口与配置：`Program.cs`、`AppConfig.cs`
- Host 架构：`Hosting/IDotBotHost.cs`、`Hosting/ServiceRegistration.cs`、`Hosting/DotBotPaths.cs`、`Hosting/CliHost.cs`、`Hosting/QQBotHost.cs`、`Hosting/WeComBotHost.cs`、`Hosting/ApiHost.cs`
- Agent 构建：`Agents/AgentFactory.cs`、`Agents/SubAgentManager.cs`
- 上下文与记忆：`Context/MemoryContextProvider.cs`、`Context/PromptBuilder.cs`、`Memory/MemoryStore.cs`、`Memory/SessionStore.cs`
- 工具：`Tools/AgentTools.cs`、`Tools/FileTools.cs`、`Tools/ShellTools.cs`、`Tools/WebTools.cs`、`Tools/WeComTools.cs`
- MCP：`Mcp/McpServerConfig.cs`、`Mcp/McpClientManager.cs`
- 心跳与定时：`Heartbeat/HeartbeatService.cs`、`Cron/CronService.cs`、`Cron/CronTypes.cs`、`Cron/CronTools.cs`
- 安全：`Security/IApprovalService.cs`、`Security/PathBlacklist.cs`、`Security/ShellCommandInspector.cs`、`Security/ConsoleApprovalService.cs`、`Security/ApprovalStore.cs`
- QQ 集成：`QQ/OneBotReverseWsServer.cs`、`QQ/QQBotClient.cs`、`QQ/QQChannelAdapter.cs`、`QQ/QQApprovalService.cs`、`QQ/QQPermissionService.cs`
- WeCom 集成：`WeCom/WeComBotServer.cs`、`WeCom/WeComChannelAdapter.cs`、`WeCom/WeComApprovalService.cs`、`WeCom/WeComPermissionService.cs`、`WeCom/WeComPusher.cs`
- Skills：`Skills/SkillsLoader.cs`

## 相关文档

- [配置与安全指南](./config_guide.md)
- [API 模式指南](./api_guide.md)
- [QQ 机器人指南](./qq_bot_guide.md)
- [企业微信指南](./wecom_guide.md)
- [文档索引](./index.md)

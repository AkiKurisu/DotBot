# DotBot 架构与安全

DotBot 是一个通用型、工具驱动的智能体系统，采用模块化架构设计，可运行在本地 REPL、QQ 机器人、企业微信机器人和 API 服务四种形态。其设计目标是"可控、可扩展、可审计"，通过安全层与审批机制约束高风险操作。

- 适用场景：开发协作助手、受控自动化执行（文件、Shell、Web）、团队 QQ/微信助手、API 服务集成
- 运行形态：CLI（REPL）、QQ（OneBot V11 反向 WebSocket）、WeCom Bot（企业微信 HTTP 回调）、API（OpenAI 兼容 HTTP 服务）
- 扩展方式：Tools 与 Skills（工作区 skills/ 下放置 SKILL.md 文本技能）、MCP 外部工具服务

---

## 架构总览

```
DotBot/
├─ Program.cs                 # 入口：校验工作区、加载 AppConfig、HostBuilder 选择模块
├─ AppConfig.cs               # 分层配置（全局 ~/.bot + 工作区 .bot/appsettings.json）
│
├─ Abstractions/              # 核心抽象接口
│  ├─ IDotBotModule.cs        # 模块接口：定义渠道/模式的抽象
│  ├─ IHostFactory.cs         # Host 工厂接口
│  ├─ IAgentToolProvider.cs   # 工具提供者接口
│  ├─ IApprovalServiceFactory.cs # 审批服务工厂接口
│  ├─ ModuleContext.cs        # 模块上下文
│  ├─ ToolProviderContext.cs  # 工具提供者上下文
│  ├─ ApprovalServiceContext.cs # 审批服务上下文
│  └─ ModuleBase.cs           # 模块基类
│
├─ Modules/                   # 模块定义
│  ├─ Registry/
│  │  └─ ModuleRegistry.cs    # 模块注册表：发现、注册、选择模块
│  ├─ CliModule.cs            # CLI 模块
│  ├─ ApiModule.cs            # API 模块
│  ├─ QQModule.cs             # QQ 模块
│  └─ WeComModule.cs          # 企业微信模块
│
├─ Hosting/                   # 主机相关
│  └─ HostBuilder.cs          # 启动器：协调模块选择与服务配置
│
├─ Commands/                  # 命令系统
│  ├─ Core/
│  │  ├─ ICommandHandler.cs   # 命令处理器接口
│  │  ├─ ICommandResponder.cs # 命令响应器接口
│  │  ├─ CommandDispatcher.cs # 命令分发器
│  │  ├─ CommandContext.cs    # 命令上下文
│  │  └─ CommandResult.cs     # 命令执行结果
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
├─ Configuration/             # 配置映射系统
│  ├─ Contracts/
│  │  ├─ IModuleConfigBinder.cs   # 模块配置绑定器接口
│  │  └─ IModuleConfigProvider.cs # 模块配置提供者接口
│  ├─ Core/
│  │  └─ ModuleConfigProvider.cs  # 模块配置提供者实现
│  ├─ Modules/
│  │  ├─ QQModuleConfig.cs
│  │  ├─ WeComModuleConfig.cs
│  │  ├─ ApiModuleConfig.cs
│  │  └─ CliModuleConfig.cs
│  └─ Binders/
│     └─ ...                    # 各模块配置绑定器
│
├─ Hosting/
│  ├─ IDotBotHost.cs       # Host 接口：RunAsync + IAsyncDisposable
│  ├─ ServiceRegistration.cs  # DI 服务注册扩展方法（AddDotBot）
│  ├─ DotBotPaths.cs       # 路径配置（WorkspacePath/DotBotPath）
│  ├─ CliHost.cs              # CLI（REPL）模式 Host
│  ├─ QQBotHost.cs            # QQ 机器人模式 Host
│  ├─ WeComBotHost.cs         # 企业微信机器人模式 Host
│  ├─ ApiHost.cs              # API 模式 Host（OpenAI 兼容）
│  └─ ApiApprovalService.cs   # API 模式审批服务
│
├─ Agents/
│  ├─ AgentFactory.cs         # 构建 AIAgent：聚合 IAgentToolProvider，注入 Memory/Skills
│  └─ SubAgentManager.cs      # 子智能体（工具受限）
│
├─ Tools/
│  ├─ Providers/              # 工具提供者（分层）
│  │  ├─ Core/
│  │  │  └─ CoreToolProvider.cs    # 核心工具：File/Shell/Web/Agent
│  │  ├─ Channels/
│  │  │  ├─ QQToolProvider.cs      # QQ 渠道工具
│  │  │  └─ WeComToolProvider.cs   # 企业微信渠道工具
│  │  └─ System/
│  │     ├─ CronToolProvider.cs    # 定时任务工具
│  │     └─ McpToolProvider.cs     # MCP 工具
│  ├─ AgentTools.cs           # SpawnSubagent
│  ├─ FileTools.cs            # 文件读写/编辑/列表
│  ├─ ShellTools.cs           # Shell 执行
│  ├─ WebTools.cs             # Web 搜索与抓取
│  └─ WeComTools.cs           # 企业微信通知
│
├─ Mcp/
│  ├─ McpServerConfig.cs      # MCP 服务器配置模型
│  └─ McpClientManager.cs     # MCP 客户端生命周期管理
│
├─ Heartbeat/
│  └─ HeartbeatService.cs     # 心跳服务
│
├─ Cron/
│  ├─ CronService.cs          # 定时任务调度
│  ├─ CronTypes.cs            # 数据模型
│  └─ CronTools.cs            # Agent 工具
│
├─ Security/
│  ├─ IApprovalService.cs     # 审批接口
│  ├─ ConsoleApprovalService  # CLI 审批交互
│  ├─ PathBlacklist.cs        # 路径黑名单
│  ├─ ShellCommandInspector   # Shell 命令路径静态分析
│  ├─ ApprovalContextScope.cs # AsyncLocal 审批上下文
│  └─ ApprovalStore.cs        # 审批决策持久化
│
├─ Memory/
│  ├─ MemoryStore.cs          # 记忆系统
│  └─ SessionStore.cs         # 会话持久化
│
├─ Context/
│  ├─ MemoryContextProvider   # AIContextProvider
│  └─ PromptBuilder.cs        # 组装系统提示词
│
├─ QQ/
│  ├─ Factories/
│  │  ├─ QQClientFactory.cs       # QQ 客户端工厂
│  │  └─ QQApprovalServiceFactory.cs # QQ 审批服务工厂
│  ├─ OneBotReverseWsServer   # 反向 WS 服务端
│  ├─ QQBotClient.cs          # 事件/动作封装
│  ├─ QQChannelAdapter.cs     # 消息处理
│  ├─ QQApprovalService.cs    # QQ 审批交互
│  └─ QQPermissionService.cs  # 权限分级
│
├─ WeCom/
│  ├─ Factories/
│  │  ├─ WeComClientFactory.cs    # 企业微信客户端工厂
│  │  └─ WeComApprovalServiceFactory.cs # 企业微信审批服务工厂
│  ├─ WeComBotServer.cs       # HTTP 服务端
│  ├─ WeComBotRegistry.cs     # 多机器人路由
│  ├─ WeComChannelAdapter.cs  # 消息处理
│  ├─ WeComApprovalService.cs # 审批交互
│  └─ WeComPermissionService.cs # 权限分级
│
├─ Api/
│  └─ Factories/
│     └─ ApiApprovalServiceFactory.cs # API 审批服务工厂
│
├─ CLI/
│  ├─ Factories/
│  │  └─ ConsoleApprovalServiceFactory.cs # 控制台审批服务工厂
│  ├─ ReplHost.cs             # REPL 循环
│  ├─ InitHelper.cs           # 工作区初始化
│  └─ Rendering/*             # 事件渲染
│
└─ Skills/
   └─ SkillsLoader.cs         # 技能加载器
```

---

## 模块化架构

### 核心概念

DotBot 采用模块化架构，每个运行形态（CLI、API、QQ、WeCom）都是一个独立的模块：

```
┌─────────────────────────────────────────────────────────────────┐
│                         Program.cs                              │
│                     (入口 + 配置加载)                            │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                        HostBuilder                              │
│                      (启动器)                                    │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                      ModuleRegistry                             │
│          (模块发现 + 注册 + 选择)                                │
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
│                    (创建 IDotBotHost)                           │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                       IDotBotHost                               │
│              (CliHost / ApiHost / QQBotHost / WeComBotHost)     │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                    IAgentToolProvider                           │
│                (CoreToolProvider + 渠道工具)                    │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                      AgentFactory                               │
│              (聚合工具 + 构建 AIAgent)                          │
└─────────────────────────────────────────────────────────────────┘
```

### 模块接口

```csharp
public interface IDotBotModule
{
    string Name { get; }           // 模块名称（cli, api, qq, wecom）
    int Priority => 0;             // 优先级（高优先级模块优先选择）
    bool IsEnabled(AppConfig config); // 根据配置判断是否启用
    void ConfigureServices(IServiceCollection services, ModuleContext context);
}
```

### 模块优先级

| 模块 | 优先级 | 启用条件 |
|------|--------|----------|
| CLI | 0 | 其他模块均未启用时（默认回退） |
| API | 10 | `Api.Enabled = true` |
| WeCom | 20 | `WeComBot.Enabled = true` |
| QQ | 30 | `QQBot.Enabled = true` |

### 模块发现机制

模块注册支持两种发现方式：

1. **源码生成器**（优先）：通过 `DotBotModuleAttribute` 和 `HostFactoryAttribute` 特性标记，编译时自动生成注册代码
2. **反射**（兜底）：运行时扫描程序集，查找实现 `IDotBotModule` 和 `IHostFactory` 的类型

```csharp
// 模块标记示例
[DotBotModule("cli", Priority = 0, Description = "CLI 模块")]
public sealed class CliModule : ModuleBase { ... }

[HostFactory("cli")]
public sealed class CliHostFactory : IHostFactory { ... }
```

---

## 命令系统

### 架构

命令系统采用统一的处理模型，消除各渠道重复的命令处理逻辑：

```
┌─────────────────────────────────────────────────────────────────┐
│                    CommandDispatcher                            │
│                     (命令分发器)                                 │
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
│           (QQCommandResponder / WeComCommandResponder)          │
└─────────────────────────────────────────────────────────────────┘
```

### 核心接口

```csharp
// 命令处理器
public interface ICommandHandler
{
    string[] Commands { get; }  // 支持的命令（如 "/new", "/clear"）
    Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder);
}

// 命令响应器（渠道适配）
public interface ICommandResponder
{
    Task SendTextAsync(string text);
    Task SendMarkdownAsync(string markdown);
}
```

### 内置命令

| 命令 | 处理器 | 说明 |
|------|--------|------|
| `/new` | NewCommandHandler | 开始新会话 |
| `/help` | HelpCommandHandler | 显示帮助信息 |
| `/debug` | DebugCommandHandler | 切换调试模式 |
| `/heartbeat` | HeartbeatCommandHandler | 触发心跳检查 |
| `/cron` | CronCommandHandler | 定时任务管理 |

---

## 工具提供者系统

### 分层设计

工具按职责分层注册，支持灵活组合：

```
┌─────────────────────────────────────────────────────────────────┐
│                    AgentFactory                                 │
│                  (工具聚合 + Agent 构建)                         │
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

### 工具提供者接口

```csharp
public interface IAgentToolProvider
{
    int Priority => 100;  // 优先级（数值小的先注册）
    IEnumerable<AITool> CreateTools(ToolProviderContext context);
}
```

### 工具优先级

| 提供者 | 优先级 | 说明 |
|--------|--------|------|
| CoreToolProvider | 10 | 核心工具（File/Shell/Web/Agent） |
| WeComToolProvider | 50 | 企业微信通知工具 |
| QQToolProvider | 50 | QQ 相关工具 |
| CronToolProvider | 100 | 定时任务工具 |
| McpToolProvider | 100 | MCP 工具 |

---

## 配置映射系统

### 模块配置绑定

每个模块可以有独立的配置模型和绑定器：

```csharp
public interface IModuleConfigBinder<TConfig>
{
    string SectionName { get; }  // 配置节名称
    TConfig Bind(AppConfig appConfig);  // 从 AppConfig 绑定
    IReadOnlyList<string> Validate(TConfig config);  // 验证配置
}
```

### 配置模型

```
AppConfig (主配置)
    ├── QQModuleConfig (QQBot.* 映射)
    ├── WeComModuleConfig (WeComBot.* 映射)
    ├── ApiModuleConfig (Api.* 映射)
    └── CliModuleConfig (CLI 相关配置)
```

---

## 关键路径

1. **启动**：`Program.cs` → 检测工作区 → `AppConfig.LoadWithGlobalFallback`
2. **模块发现**：`ModuleRegistry` → 源码生成器或反射发现模块
3. **模块选择**：`HostBuilder.Build()` → 按优先级选择启用的模块
4. **服务配置**：`module.ConfigureServices()` → 注册模块特有服务
5. **Host 创建**：`IHostFactory.CreateHost()` → 创建 `IDotBotHost` 实例
6. **Agent 构建**：`AgentFactory` → 聚合 `IAgentToolProvider` → 注册工具
7. **运行**：`host.RunAsync()` → 进入渠道事件循环

---

## 配置系统

DotBot 采用分层配置，工作区配置覆盖全局配置：

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `ApiKey` | — | OpenAI 兼容 API Key（必填） |
| `Model` | `gpt-4o-mini` | 使用的模型 |
| `EndPoint` | `https://api.openai.com/v1` | API 端点 |
| `SystemInstructions` | 内置中文提示 | 系统提示词 |
| `MaxToolCallRounds` | 30 | 主 Agent 最大工具调用轮数 |
| `SubagentMaxToolCallRounds` | 15 | 子智能体最大工具调用轮数 |
| `Tools.File.RequireApprovalOutsideWorkspace` | true | 文件越界时走审批 |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | true | 命令越界时走审批 |
| `Tools.Shell.Timeout` | 60 | Shell 命令超时（秒） |
| `Security.BlacklistedPaths` | [] | 路径黑名单列表 |
| `QQBot.Enabled` | false | 是否启用 QQ 机器人模式 |
| `WeCom.Enabled` | false | 是否启用企业微信通知 |
| `WeCom.WebhookUrl` | — | 企业微信群机器人 Webhook URL |

详细配置说明见 [配置指南](./config_guide.md)。

---

## 扩展指南

### 添加新模块

1. **创建模块类**：

```csharp
[DotBotModule("mymodule", Priority = 40, Description = "我的模块")]
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
        // 注册模块特有服务
        services.AddSingleton<IMyService, MyService>();
    }
}
```

2. **创建 Host 工厂**：

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

3. **创建 Host 实现**：

```csharp
public sealed class MyHost : IDotBotHost
{
    public async Task RunAsync() { /* 实现运行逻辑 */ }
    public ValueTask DisposeAsync() { /* 清理资源 */ }
}
```

### 添加新工具提供者

```csharp
public sealed class MyToolProvider : IAgentToolProvider
{
    public int Priority => 60;  // 在核心工具之后，系统工具之前
    
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var myTools = new MyTools(context.Config);
        yield return AIFunctionFactory.Create(myTools.MyFunction);
    }
}
```

### 添加新命令处理器

```csharp
public sealed class MyCommandHandler : ICommandHandler
{
    public string[] Commands => ["/mycommand"];
    
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        await responder.SendTextAsync("执行我的命令");
        return CommandResult.Handled;
    }
}
```

---

## 记忆与会话

- **MemoryStore**（`Memory/MemoryStore.cs`）：
  - 每日笔记：`<.bot>/memory/YYYY-MM-DD.md`
  - 长期记忆：`<.bot>/memory/MEMORY.md`
  - 上下文注入：每轮对话前组合注入 SystemPrompt

- **SessionStore**（`Memory/SessionStore.cs`）：
  - 基于 `Microsoft.Agents.AI.AgentSession` 的序列化
  - 会话文件：`<.bot>/sessions/<id>.json`
  - 支持会话压缩（移除 tool 消息，减少存储）

---

## 安全模型

### 1) 路径黑名单

- FileTools：黑名单路径直接拒绝
- ShellTools：命令字符串引用黑名单路径时拒绝

### 2) 工作区边界

- **FileTools**：工作区外访问 → 配置为"拒绝"或"发起审批"
- **ShellTools**：静态分析命令字符串，识别绝对路径、家目录、HOME 变量

### 3) 危险命令

- deny patterns：`rm -rf`、`mkfs`、`dd`、`shutdown`、fork bomb 等
- 超时（默认 60s）与输出上限（默认 10000 字符）

### 4) 审批流

- **CLI**：Always/Session/Once/Reject，决策持久化
- **QQ**：群聊内发起审批，@提醒，关键字"同意/拒绝"
- **WeCom**：会话内发起审批，关键字"同意/同意全部/拒绝"

### 5) 角色与操作分级

- 角色：`Admin` / `Whitelisted` / `Unauthorized`
- 操作级别：Tier 0（Chat）→ Tier 1（ReadOnly）→ Tier 2（WriteWorkspace）→ Tier 3（WriteOutsideWorkspace）

详见 [QQ 机器人指南](./qq_bot_guide.md) 和 [企业微信指南](./wecom_guide.md)。

---

## 运行形态

### CLI（REPL）

- 单用户本地开发与调试
- 审批走控制台（Always/Session/Once/Reject）
- 会话管理：`/session`、`/memory`、`/skills` 等命令

### QQ Bot

- 群聊/私聊入口，NapCat 反向 WebSocket 连接
- 权限分级：基于 QQ 号 + 群号配置
- 会话隔离：每个 QQ 号独立会话

### WeCom Bot

- 群聊/单聊入口，HTTP 服务器接收消息回调
- 权限分级：基于企业微信 UserId + ChatId 配置
- 会话隔离：每个用户+会话独立

### API（OpenAI 兼容服务）

- 基于 Microsoft.Agents.AI.Hosting.OpenAI 框架
- 端点：`POST /dotbot/v1/chat/completions`
- Bearer Token 认证（可选）
- 工具过滤：通过 `EnabledTools` 配置

---

## Skills（文本技能）

- 位置：`<workspace>/skills/<name>/SKILL.md` 或 `~/.bot/skills/<name>/SKILL.md`
- 前置元数据：`always`、`bins`、`env`
- 加载逻辑：工作区优先，`always=true` 自动注入

---

## 能力边界与建议

- **文件**：工作区内读写；黑名单拦截；单文件上限 10MB
- **Shell**：禁止危险命令；越界检测与审批；超时 60s
- **Web**：搜索默认 5 条结果；抓取上限 50000 字符
- **子智能体**：工具受限；工作区外直接拒绝；迭代上限 15 轮
- **Heartbeat**：定时读取 HEARTBEAT.md 执行
- **Cron**：定时任务调度（at/every），JSON 持久化
- **MCP**：接入外部工具服务器

---

## 参考源码

所有路径相对于 `DotBot/`：

- 入口与配置：`Program.cs`、`AppConfig.cs`
- 抽象接口：`Abstractions/IDotBotModule.cs`、`Abstractions/IHostFactory.cs`、`Abstractions/IAgentToolProvider.cs`
- 模块系统：`Modules/Registry/ModuleRegistry.cs`、`Modules/CliModule.cs`、`Modules/QQModule.cs`、`Modules/WeComModule.cs`、`Modules/ApiModule.cs`
- 启动器：`Hosting/HostBuilder.cs`
- 命令系统：`Commands/Core/CommandDispatcher.cs`、`Commands/Core/ICommandHandler.cs`、`Commands/Handlers/*.cs`
- 配置映射：`Configuration/Contracts/IModuleConfigBinder.cs`、`Configuration/Core/ModuleConfigProvider.cs`
- Host 架构：`Hosting/IDotBotHost.cs`、`Hosting/ServiceRegistration.cs`、`Hosting/CliHost.cs`、`Hosting/QQBotHost.cs`、`Hosting/WeComBotHost.cs`、`Hosting/ApiHost.cs`
- Agent 构建：`Agents/AgentFactory.cs`、`Agents/SubAgentManager.cs`
- 工具提供者：`Tools/Providers/Core/CoreToolProvider.cs`、`Tools/Providers/Channels/*.cs`、`Tools/Providers/System/*.cs`
- 工具实现：`Tools/AgentTools.cs`、`Tools/FileTools.cs`、`Tools/ShellTools.cs`、`Tools/WebTools.cs`
- MCP：`Mcp/McpServerConfig.cs`、`Mcp/McpClientManager.cs`
- 心跳与定时：`Heartbeat/HeartbeatService.cs`、`Cron/CronService.cs`、`Cron/CronTools.cs`
- 安全：`Security/IApprovalService.cs`、`Security/PathBlacklist.cs`、`Security/ShellCommandInspector.cs`
- QQ 集成：`QQ/Factories/*.cs`、`QQ/QQBotClient.cs`、`QQ/QQChannelAdapter.cs`
- WeCom 集成：`WeCom/Factories/*.cs`、`WeCom/WeComBotServer.cs`、`WeCom/WeComChannelAdapter.cs`
- Skills：`Skills/SkillsLoader.cs`

## 相关文档

- [配置与安全指南](./config_guide.md)
- [API 模式指南](./api_guide.md)
- [QQ 机器人指南](./qq_bot_guide.md)
- [企业微信指南](./wecom_guide.md)
- [文档索引](./index.md)

# DotBot API 模式指南

DotBot API 模式将 Agent 能力通过 **OpenAI 兼容的 HTTP API** 暴露，外部应用可直接使用标准 OpenAI SDK（Python、JavaScript、.NET 等）调用 DotBot 进行推理和工具调用，无需自定义 SDK。

## 架构概览

```
外部应用 ──── OpenAI SDK ──── HTTP ──── DotBot API Server
  (Python/JS/...)                        ↑ Microsoft.Agents.AI.Hosting.OpenAI
                                         ↑ OpenAI Chat Completions 协议
```

- **协议**: OpenAI Chat Completions API（`/v1/chat/completions`）
- **框架**: 基于 [Microsoft.Agents.AI.Hosting.OpenAI](https://github.com/microsoft/agent-framework) 官方框架
- **传输**: HTTP，支持 streaming（`"stream": true`）和非 streaming 两种模式
- **认证**: Bearer Token（可选）

---

## 快速开始

### 1. 配置

在 `appsettings.json` 中启用 API 模式：

```json
{
    "ApiKey": "sk-your-llm-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "Api": {
        "Enabled": true,
        "Host": "0.0.0.0",
        "Port": 8080,
        "ApiKey": "your-api-access-key",
        "AutoApprove": true
    }
}
```

### 2. 启动

```bash
cd /your/workspace
dotbot
```

启动成功后控制台输出：

```
DotBot API listening on http://0.0.0.0:8080
Endpoints (OpenAI-compatible):
  POST /dotbot/v1/chat/completions
Additional endpoints:
  GET  /v1/health
  GET  /v1/tools
  GET  /v1/sessions
  DELETE /v1/sessions/{id}
All tools enabled (9 tools)
Press Ctrl+C to stop...
```

### 3. 调用

使用任何 OpenAI 兼容的 SDK 即可调用：

**Python**

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:8080/dotbot/v1",
    api_key="your-api-access-key"
)

response = client.chat.completions.create(
    model="dotbot",
    messages=[
        {"role": "user", "content": "搜索最新的 AI 新闻"}
    ]
)

print(response.choices[0].message.content)
```

**Python (Streaming)**

```python
stream = client.chat.completions.create(
    model="dotbot",
    messages=[
        {"role": "user", "content": "分析当前目录的项目结构"}
    ],
    stream=True
)

for chunk in stream:
    if chunk.choices[0].delta.content:
        print(chunk.choices[0].delta.content, end="")
```

**JavaScript/TypeScript**

```typescript
import OpenAI from 'openai';

const client = new OpenAI({
    baseURL: 'http://localhost:8080/dotbot/v1',
    apiKey: 'your-api-access-key',
});

const response = await client.chat.completions.create({
    model: 'dotbot',
    messages: [
        { role: 'user', content: '列出工作区中的文件' }
    ],
});

console.log(response.choices[0].message.content);
```

**curl**

```bash
curl -X POST http://localhost:8080/dotbot/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer your-api-access-key" \
  -d '{
    "model": "dotbot",
    "messages": [
      {"role": "user", "content": "hello"}
    ]
  }'
```

**curl (Streaming)**

```bash
curl -X POST http://localhost:8080/dotbot/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer your-api-access-key" \
  -d '{
    "model": "dotbot",
    "messages": [
      {"role": "user", "content": "搜索最新的 AI 新闻"}
    ],
    "stream": true
  }'
```

---

## 配置项

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Api.Enabled` | bool | `false` | 是否启用 API 模式 |
| `Api.Host` | string | `"0.0.0.0"` | HTTP 服务监听地址 |
| `Api.Port` | int | `8080` | HTTP 服务监听端口 |
| `Api.ApiKey` | string | 空 | API 访问密钥（Bearer Token），为空时不验证 |
| `Api.AutoApprove` | bool | `true` | 是否自动批准所有文件/Shell 操作（被 ApprovalMode 覆盖） |
| `Api.ApprovalMode` | string | 空 | 审批模式：`auto`/`reject`/`interactive`，设置后覆盖 AutoApprove |
| `Api.ApprovalTimeoutSeconds` | int | `120` | interactive 模式下审批请求超时时间（秒） |
| `Api.EnabledTools` | array | `[]` | 启用的工具列表，为空时启用所有工具 |

### 运行模式优先级

| 条件 | 运行模式 |
|------|----------|
| `QQBot.Enabled = true` | QQ 机器人模式 |
| `WeComBot.Enabled = true` | 企业微信机器人模式 |
| `Api.Enabled = true` | API 模式 |
| 其他 | CLI 模式 |

> **注意**：QQ Bot、WeCom Bot 和 API 模式不能同时启用，按上述优先级选择。

---

## 工具过滤

API 模式支持通过 `EnabledTools` 配置项选择性暴露工具。这对于将 DotBot 作为专用服务（如仅提供网络搜索能力）非常有用。

### 所有内置工具名称

| 工具名 | 说明 |
|--------|------|
| `SpawnSubagent` | 创建子智能体执行子任务 |
| `ReadFile` | 读取文件内容 |
| `WriteFile` | 写入文件 |
| `EditFile` | 编辑文件 |
| `GrepFiles` | 搜索文件内容 |
| `FindFiles` | 查找文件 |
| `Exec` | 执行 Shell 命令 |
| `WebSearch` | 网络搜索 |
| `WebFetch` | 抓取网页内容 |
| `Cron` | 定时任务管理（需启用 Cron） |
| `WeComNotify` | 企业微信通知（需启用 WeCom） |

> MCP 服务器注册的工具也可以通过工具名过滤。

### 配置示例

**仅暴露网络搜索工具**（适用于搜索服务场景）：

```json
{
    "Api": {
        "Enabled": true,
        "EnabledTools": ["WebSearch", "WebFetch"]
    }
}
```

**仅暴露文件工具**（适用于代码分析场景）：

```json
{
    "Api": {
        "Enabled": true,
        "EnabledTools": ["ReadFile", "GrepFiles", "FindFiles"]
    }
}
```

**启用所有工具**（默认行为，`EnabledTools` 为空数组或不设置）：

```json
{
    "Api": {
        "Enabled": true,
        "EnabledTools": []
    }
}
```

---

## 认证

### Bearer Token 认证

当 `Api.ApiKey` 配置为非空值时，所有对 `/dotbot/` 路径的请求都需要携带 Bearer Token：

```
Authorization: Bearer your-api-access-key
```

未通过认证的请求会返回 `401 Unauthorized`。

辅助端点（`/v1/health`、`/v1/tools`、`/v1/sessions`）也需要认证（`/v1/health` 除外）。

### 禁用认证

将 `Api.ApiKey` 设置为空字符串或不设置，即可禁用认证。适用于本地开发或内网部署。

> **安全建议**：生产环境务必配置 `ApiKey`，避免未授权访问。

---

## 操作审批

API 模式通过 `ApiApprovalService` 处理操作审批，支持三种模式：

| 模式 | 配置 | 行为 |
|------|------|------|
| **auto** | `"ApprovalMode": "auto"` 或 `"AutoApprove": true` | 所有文件操作和 Shell 命令自动批准 |
| **reject** | `"ApprovalMode": "reject"` 或 `"AutoApprove": false` | 所有文件操作和 Shell 命令自动拒绝 |
| **interactive** | `"ApprovalMode": "interactive"` | Human-in-the-Loop：敏感操作暂停等待 API 客户端审批 |

> `ApprovalMode` 设置后会覆盖 `AutoApprove`。

### Human-in-the-Loop 交互式审批

当 `ApprovalMode` 设为 `"interactive"` 时，Agent 执行敏感操作（工作区外的文件访问、Shell 命令）会暂停并创建待审批请求，等待 API 客户端通过审批端点进行审批。

**流程**：

```
客户端发送聊天请求
    ↓
Agent 执行工具 → 遇到需审批操作 → 暂停
    ↓
客户端轮询 GET /v1/approvals → 获取待审批列表
    ↓
客户端发送 POST /v1/approvals/{id} → 批准/拒绝
    ↓
Agent 恢复执行 → 返回结果
```

**配置**：

```json
{
    "Api": {
        "Enabled": true,
        "ApprovalMode": "interactive",
        "ApprovalTimeoutSeconds": 120
    }
}
```

**审批端点**：

#### GET /v1/approvals

获取所有待审批请求。

```bash
curl -H "Authorization: Bearer your-key" http://localhost:8080/v1/approvals
```

响应：
```json
{
    "approvals": [
        {
            "id": "a1b2c3d4e5f6",
            "type": "file",
            "operation": "write",
            "detail": "/path/to/file.txt",
            "createdAt": "2025-01-01T00:00:00.0000000Z"
        }
    ]
}
```

#### POST /v1/approvals/{id}

批准或拒绝一个待审批请求。

```bash
# 批准
curl -X POST -H "Authorization: Bearer your-key" \
  -H "Content-Type: application/json" \
  -d '{"approved": true}' \
  http://localhost:8080/v1/approvals/a1b2c3d4e5f6

# 拒绝
curl -X POST -H "Authorization: Bearer your-key" \
  -H "Content-Type: application/json" \
  -d '{"approved": false}' \
  http://localhost:8080/v1/approvals/a1b2c3d4e5f6
```

**Python 示例**：

完整的 Human-in-the-Loop Python 示例见 [`Samples/python/human_in_the_loop.py`](../Samples/python/human_in_the_loop.py)。

> **超时**：如果在 `ApprovalTimeoutSeconds`（默认 120 秒）内未收到审批决定，操作会自动被拒绝。

> **安全建议**：如果 DotBot 部署在公网，建议使用 `"interactive"` 或 `"reject"` 模式，并仅启用安全的工具（如 `web_search`）。

---

## 辅助端点

除了 OpenAI 兼容的主端点外，API 模式还提供以下辅助端点：

### GET /v1/health

健康检查，不需要认证。

```bash
curl http://localhost:8080/v1/health
```

响应：
```json
{
    "status": "ok",
    "version": "1.0.0",
    "mode": "api",
    "model": "gpt-4o-mini",
    "protocol": "openai-compatible"
}
```

### GET /v1/approvals

获取待审批请求列表（仅 `ApprovalMode: "interactive"` 时有效）。

```bash
curl -H "Authorization: Bearer your-key" http://localhost:8080/v1/approvals
```

### POST /v1/approvals/{id}

批准或拒绝待审批请求。

```bash
curl -X POST -H "Authorization: Bearer your-key" \
  -H "Content-Type: application/json" \
  -d '{"approved": true}' \
  http://localhost:8080/v1/approvals/a1b2c3d4e5f6
```

### GET /v1/tools

列出当前启用的工具。

```bash
curl -H "Authorization: Bearer your-key" http://localhost:8080/v1/tools
```

响应：
```json
{
    "tools": [
        {"name": "web_search", "icon": "🔍"},
        {"name": "web_fetch", "icon": "🌐"},
        {"name": "read_file", "icon": "📄"}
    ]
}
```

### GET /v1/sessions

列出所有会话。

```bash
curl -H "Authorization: Bearer your-key" http://localhost:8080/v1/sessions
```

### DELETE /v1/sessions/{id}

删除指定会话。

```bash
curl -X DELETE -H "Authorization: Bearer your-key" http://localhost:8080/v1/sessions/session-id
```

---

## 部署示例

### 本地开发

```json
{
    "ApiKey": "sk-your-llm-key",
    "Model": "gpt-4o-mini",
    "Api": {
        "Enabled": true,
        "Port": 8080,
        "AutoApprove": true
    }
}
```

### 内网搜索服务

仅暴露网络搜索能力，关闭文件和 Shell 访问：

```json
{
    "ApiKey": "sk-your-llm-key",
    "Model": "gpt-4o-mini",
    "Api": {
        "Enabled": true,
        "Port": 8080,
        "ApiKey": "internal-api-key",
        "AutoApprove": false,
        "EnabledTools": ["web_search", "web_fetch"]
    }
}
```

### 公网部署（搭配反向代理）

建议使用 Nginx/Caddy 作为反向代理，处理 HTTPS 和速率限制：

```nginx
server {
    listen 443 ssl;
    server_name dotbot.example.com;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Connection "";
        proxy_set_header Host $host;
        proxy_buffering off;
        proxy_cache off;
    }
}
```

此时客户端使用：

```python
client = OpenAI(
    base_url="https://dotbot.example.com/dotbot/v1",
    api_key="your-api-access-key"
)
```

---

## 工作区说明

API 模式下，Agent 的工具操作的是 **DotBot 进程所在的工作区**：

- 如果在本地机器启动 `dotbot --api`，工具操作本地文件系统
- 如果部署在远端服务器，工具操作远端文件系统

这与 Claude Code 等工具的设计一致：Agent 运行在哪里，工具就操作哪里的环境。

---

## 与 Heartbeat/Cron 集成

API 模式下仍然支持 Heartbeat 心跳服务和 Cron 定时任务：

```json
{
    "Api": {
        "Enabled": true,
        "Port": 8080
    },
    "Heartbeat": {
        "Enabled": true,
        "IntervalSeconds": 1800
    },
    "Cron": {
        "Enabled": true
    }
}
```

Heartbeat 和 Cron 任务会在后台自动执行，与 API 请求互不干扰。

---

## 常见问题

### Q: 支持哪些 OpenAI SDK？

任何兼容 OpenAI Chat Completions API 的 SDK 都可以使用，包括：
- Python: `openai` 库
- JavaScript/TypeScript: `openai` npm 包
- .NET: `OpenAI` NuGet 包
- Go: `sashabaranov/go-openai`
- 其他语言的 OpenAI 兼容库

### Q: API 模式和 CLI 模式可以同时运行吗？

不能。每次启动只能选择一种运行模式。如果需要同时提供 API 和 CLI，可以在不同工作区分别启动两个 DotBot 实例。

### Q: 会话如何管理？

API 模式下的会话由框架自动管理。可以通过辅助端点 `GET /v1/sessions` 查看和 `DELETE /v1/sessions/{id}` 删除会话。

### Q: 工具调用对外部客户端透明吗？

是的。工具调用在服务端完成，客户端只看到最终的文本响应。如果使用 streaming 模式，可以看到 Agent 推理过程中的文本流。

### Q: 如何限制 API 的并发请求？

当前版本不内置速率限制。建议通过反向代理（Nginx、Caddy）实现请求速率限制和并发控制。

---

## 完整配置示例

```json
{
    "ApiKey": "sk-your-llm-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "SystemInstructions": "你是 DotBot，一个简洁、可靠的智能体。",
    "MaxToolCallRounds": 30,
    "CompactSessions": true,
    "Api": {
        "Enabled": true,
        "Host": "0.0.0.0",
        "Port": 8080,
        "ApiKey": "your-api-access-key",
        "AutoApprove": true,
        "ApprovalMode": "",
        "ApprovalTimeoutSeconds": 120,
        "EnabledTools": []
    },
    "Tools": {
        "File": {
            "RequireApprovalOutsideWorkspace": true,
            "MaxFileSize": 10485760
        },
        "Shell": {
            "RequireApprovalOutsideWorkspace": true,
            "Timeout": 60,
            "MaxOutputLength": 10000
        },
        "Web": {
            "MaxChars": 50000,
            "Timeout": 30,
            "SearchMaxResults": 5,
            "SearchProvider": "Bing"
        }
    },
    "Security": {
        "BlacklistedPaths": [
            "~/.ssh",
            "~/.gnupg",
            "/etc/shadow"
        ]
    },
    "Heartbeat": {
        "Enabled": false,
        "IntervalSeconds": 1800
    },
    "Cron": {
        "Enabled": false,
        "StorePath": "cron/jobs.json"
    },
    "McpServers": []
}
```

---

## Python 示例

完整的 Python 使用示例见 [`Samples/python/`](../Samples/python/) 目录：

| 示例 | 说明 |
|------|------|
| [basic_chat.py](../Samples/python/basic_chat.py) | 基本对话（非流式） |
| [streaming_chat.py](../Samples/python/streaming_chat.py) | 流式输出 |
| [multi_turn_chat.py](../Samples/python/multi_turn_chat.py) | 多轮对话（交互式 REPL） |
| [human_in_the_loop.py](../Samples/python/human_in_the_loop.py) | Human-in-the-Loop 审批流程 |

---

## 相关文档

- [架构与安全](./architecture.md) - 架构设计、安全模型
- [配置指南](./config_guide.md) - 完整配置项说明
- [QQ 机器人指南](./qq_bot_guide.md) - QQ 机器人模式
- [企业微信指南](./wecom_guide.md) - 企业微信机器人模式
- [文档索引](./index.md) - 完整文档导航

# DotBot API Mode Guide

DotBot API mode exposes Agent capabilities via an **OpenAI-compatible HTTP API**. External applications can directly use standard OpenAI SDKs (Python, JavaScript, .NET, etc.) to call DotBot for inference and tool calling, with no custom SDK required.

## Architecture Overview

```
External App ──── OpenAI SDK ──── HTTP ──── DotBot API Server
  (Python/JS/...)                           ↑ Microsoft.Agents.AI.Hosting.OpenAI
                                            ↑ OpenAI Chat Completions Protocol
```

- **Protocol**: OpenAI Chat Completions API (`/v1/chat/completions`)
- **Framework**: Based on [Microsoft.Agents.AI.Hosting.OpenAI](https://github.com/microsoft/agent-framework) official framework
- **Transport**: HTTP, supports both streaming (`"stream": true`) and non-streaming modes
- **Authentication**: Bearer Token (optional)

---

## Quick Start

### 1. Configuration

Enable API mode in `appsettings.json`:

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

### 2. Start

```bash
cd /your/workspace
dotbot
```

Console output on successful start:

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

### 3. Call

Use any OpenAI-compatible SDK to call:

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
        {"role": "user", "content": "Search for the latest AI news"}
    ]
)

print(response.choices[0].message.content)
```

**Python (Streaming)**

```python
stream = client.chat.completions.create(
    model="dotbot",
    messages=[
        {"role": "user", "content": "Analyze the project structure in the current directory"}
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
        { role: 'user', content: 'List files in the workspace' }
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
      {"role": "user", "content": "Search for the latest AI news"}
    ],
    "stream": true
  }'
```

---

## Configuration

| Config Item | Type | Default | Description |
|-------------|------|---------|-------------|
| `Api.Enabled` | bool | `false` | Enable API mode |
| `Api.Host` | string | `"0.0.0.0"` | HTTP service listen address |
| `Api.Port` | int | `8080` | HTTP service listen port |
| `Api.ApiKey` | string | empty | API access key (Bearer Token), no verification when empty |
| `Api.AutoApprove` | bool | `true` | Whether to auto-approve all file/Shell operations (overridden by ApprovalMode) |
| `Api.ApprovalMode` | string | empty | Approval mode: `auto`/`reject`/`interactive`, overrides AutoApprove when set |
| `Api.ApprovalTimeoutSeconds` | int | `120` | Interactive mode approval timeout (seconds) |
| `Api.EnabledTools` | array | `[]` | Enabled tools list, enables all when empty |

### Runtime Mode Priority

| Condition | Runtime Mode |
|-----------|-------------|
| `QQBot.Enabled = true` | QQ Bot mode |
| `WeComBot.Enabled = true` | WeCom Bot mode |
| `Api.Enabled = true` | API mode |
| Other | CLI mode |

> **Note**: QQ Bot, WeCom Bot, and API mode cannot be enabled simultaneously. Priority is as listed above.

---

## Tool Filtering

API mode supports selectively exposing tools via the `EnabledTools` configuration. This is useful for deploying DotBot as a specialized service (e.g., providing only web search capabilities).

### All Built-in Tool Names

| Tool Name | Description |
|-----------|-------------|
| `SpawnSubagent` | Create a SubAgent to execute subtasks |
| `ReadFile` | Read file contents |
| `WriteFile` | Write to a file |
| `EditFile` | Edit a file |
| `GrepFiles` | Search file contents |
| `FindFiles` | Find files |
| `Exec` | Execute Shell commands |
| `WebSearch` | Web search |
| `WebFetch` | Fetch web page content |
| `Cron` | Scheduled task management (requires Cron enabled) |
| `WeComNotify` | WeCom notification (requires WeCom enabled) |

> MCP server-registered tools can also be filtered by tool name.

### Configuration Examples

**Expose only web search tools** (for search service scenarios):

```json
{
    "Api": {
        "Enabled": true,
        "EnabledTools": ["WebSearch", "WebFetch"]
    }
}
```

**Expose only file tools** (for code analysis scenarios):

```json
{
    "Api": {
        "Enabled": true,
        "EnabledTools": ["ReadFile", "GrepFiles", "FindFiles"]
    }
}
```

**Enable all tools** (default behavior, `EnabledTools` is empty or not set):

```json
{
    "Api": {
        "Enabled": true,
        "EnabledTools": []
    }
}
```

---

## Authentication

### Bearer Token Authentication

When `Api.ApiKey` is configured with a non-empty value, all requests to `/dotbot/` paths must carry a Bearer Token:

```
Authorization: Bearer your-api-access-key
```

Unauthenticated requests will return `401 Unauthorized`.

Auxiliary endpoints (`/v1/health`, `/v1/tools`, `/v1/sessions`) also require authentication (`/v1/health` is exempt).

### Disabling Authentication

Set `Api.ApiKey` to an empty string or leave it unset to disable authentication. Suitable for local development or intranet deployments.

> **Security Note**: Always configure `ApiKey` in production environments to prevent unauthorized access.

---

## Operation Approval

API mode handles operation approval via `ApiApprovalService`, supporting three modes:

| Mode | Configuration | Behavior |
|------|--------------|----------|
| **auto** | `"ApprovalMode": "auto"` or `"AutoApprove": true` | All file operations and Shell commands auto-approved |
| **reject** | `"ApprovalMode": "reject"` or `"AutoApprove": false` | All file operations and Shell commands auto-rejected |
| **interactive** | `"ApprovalMode": "interactive"` | Human-in-the-Loop: Sensitive operations pause waiting for API client approval |

> `ApprovalMode` overrides `AutoApprove` when set.

### Human-in-the-Loop Interactive Approval

When `ApprovalMode` is set to `"interactive"`, Agent execution of sensitive operations (file access outside workspace, Shell commands) will pause and create pending approval requests, waiting for the API client to approve via the approval endpoint.

**Flow**:

```
Client sends chat request
    ↓
Agent executes tool → encounters operation requiring approval → pauses
    ↓
Client polls GET /v1/approvals → gets pending approval list
    ↓
Client sends POST /v1/approvals/{id} → approve/reject
    ↓
Agent resumes execution → returns result
```

**Configuration**:

```json
{
    "Api": {
        "Enabled": true,
        "ApprovalMode": "interactive",
        "ApprovalTimeoutSeconds": 120
    }
}
```

**Approval Endpoints**:

#### GET /v1/approvals

Get all pending approval requests.

```bash
curl -H "Authorization: Bearer your-key" http://localhost:8080/v1/approvals
```

Response:
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

Approve or reject a pending approval request.

```bash
# Approve
curl -X POST -H "Authorization: Bearer your-key" \
  -H "Content-Type: application/json" \
  -d '{"approved": true}' \
  http://localhost:8080/v1/approvals/a1b2c3d4e5f6

# Reject
curl -X POST -H "Authorization: Bearer your-key" \
  -H "Content-Type: application/json" \
  -d '{"approved": false}' \
  http://localhost:8080/v1/approvals/a1b2c3d4e5f6
```

**Python Example**:

For a complete Human-in-the-Loop Python example, see [`Samples/python/human_in_the_loop.py`](../../Samples/python/human_in_the_loop.py).

> **Timeout**: If no approval decision is received within `ApprovalTimeoutSeconds` (default 120 seconds), the operation is automatically rejected.

> **Security Note**: If DotBot is deployed on a public network, use `"interactive"` or `"reject"` mode, and enable only safe tools (e.g., `web_search`).

---

## Auxiliary Endpoints

In addition to the OpenAI-compatible main endpoint, API mode provides the following auxiliary endpoints:

### GET /v1/health

Health check, no authentication required.

```bash
curl http://localhost:8080/v1/health
```

Response:
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

Get pending approval request list (only effective when `ApprovalMode: "interactive"`).

```bash
curl -H "Authorization: Bearer your-key" http://localhost:8080/v1/approvals
```

### POST /v1/approvals/{id}

Approve or reject a pending approval request.

```bash
curl -X POST -H "Authorization: Bearer your-key" \
  -H "Content-Type: application/json" \
  -d '{"approved": true}' \
  http://localhost:8080/v1/approvals/a1b2c3d4e5f6
```

### GET /v1/tools

List currently enabled tools.

```bash
curl -H "Authorization: Bearer your-key" http://localhost:8080/v1/tools
```

Response:
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

List all sessions.

```bash
curl -H "Authorization: Bearer your-key" http://localhost:8080/v1/sessions
```

### DELETE /v1/sessions/{id}

Delete a specified session.

```bash
curl -X DELETE -H "Authorization: Bearer your-key" http://localhost:8080/v1/sessions/session-id
```

---

## Deployment Examples

### Local Development

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

### Intranet Search Service

Expose only web search capabilities, disable file and Shell access:

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

### Public Deployment (with Reverse Proxy)

Recommended to use Nginx/Caddy as a reverse proxy for HTTPS and rate limiting:

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

Client usage:

```python
client = OpenAI(
    base_url="https://dotbot.example.com/dotbot/v1",
    api_key="your-api-access-key"
)
```

---

## Workspace Notes

In API mode, Agent tools operate on the **workspace where the DotBot process is located**:

- If DotBot is started locally with `dotbot --api`, tools operate on the local file system
- If deployed on a remote server, tools operate on the remote file system

This is consistent with the design of tools like Claude Code: wherever the Agent runs, tools operate on that environment.

---

## Heartbeat/Cron Integration

API mode still supports Heartbeat service and Cron scheduled tasks:

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

Heartbeat and Cron tasks run in the background without interfering with API requests.

---

## FAQ

### Q: Which OpenAI SDKs are supported?

Any SDK compatible with the OpenAI Chat Completions API, including:
- Python: `openai` library
- JavaScript/TypeScript: `openai` npm package
- .NET: `OpenAI` NuGet package
- Go: `sashabaranov/go-openai`
- Other language OpenAI-compatible libraries

### Q: Can API mode and CLI mode run simultaneously?

No. Only one runtime mode can be selected per launch. If you need both API and CLI, start two DotBot instances in different workspaces.

### Q: How are sessions managed?

Sessions in API mode are automatically managed by the framework. Use auxiliary endpoints `GET /v1/sessions` to view and `DELETE /v1/sessions/{id}` to delete sessions.

### Q: Are tool calls transparent to external clients?

Yes. Tool calls are completed server-side; clients only see the final text response. In streaming mode, you can see the text stream during Agent reasoning.

### Q: How to limit API concurrent requests?

The current version does not include built-in rate limiting. It is recommended to implement request rate limiting and concurrency control via a reverse proxy (Nginx, Caddy).

---

## Full Configuration Example

```json
{
    "ApiKey": "sk-your-llm-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "SystemInstructions": "You are DotBot, a concise and reliable agent.",
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

## Python Examples

For complete Python usage examples, see the [`Samples/python/`](../../Samples/python/) directory:

| Example | Description |
|---------|-------------|
| [basic_chat.py](../../Samples/python/basic_chat.py) | Basic chat (non-streaming) |
| [streaming_chat.py](../../Samples/python/streaming_chat.py) | Streaming output |
| [multi_turn_chat.py](../../Samples/python/multi_turn_chat.py) | Multi-turn conversation (interactive REPL) |
| [human_in_the_loop.py](../../Samples/python/human_in_the_loop.py) | Human-in-the-Loop approval flow |

---

## Related Documentation

- [Configuration Guide](./config_guide.md) - Complete configuration reference
- [QQ Bot Guide](./qq_bot_guide.md) - QQ Bot mode
- [WeCom Guide](./wecom_guide.md) - WeCom Bot mode
- [Documentation Index](./index.md) - Full documentation navigation

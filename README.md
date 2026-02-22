<div align="center">

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/AkiKurisu/DotBot)
[![Zhihu](https://img.shields.io/badge/知乎-AkiKurisu-0084ff?style=flat-square)](https://www.zhihu.com/people/akikurisu)
[![Bilibili](https://img.shields.io/badge/Bilibili-爱姬Kurisu-00A1D6?style=flat-square)](https://space.bilibili.com/20472331)

**[中文](./README_ZH.md) | English**

# DotBot

**DotBot** (.Bot) is a lightweight OpenClaw written in C# that is secure, reliable, and ready to use out of the box.

![banner](./Documentation/images/banner.png)

</div>

## ✨ Features

<table>
<tr>
<td width="33%" align="center"><b>🪶 Lightweight & Minimal</b><br/>Written in C#，built on .NET 10, single-file, no complex dependencies.</td>
<td width="33%" align="center"><b>🚀 One-Click Deployment</b><br/>No complicated configuration process required.</td>
<td width="33%" align="center"><b>🔒 Secure Approval</b><br/>Multi-layer security with approval flow for high-risk operations.</td>
</tr>
</table>

- 🛠️ **Tool Capabilities**: File read/write (workspace-scoped), controlled Shell commands, Web scraping, optional SubAgent delegation
- 🔌 **MCP Integration**: Connect external tool services via [Model Context Protocol](https://modelcontextprotocol.io/)
- 🎮 **Multiple Runtime Modes**: Local REPL, QQ Bot (OneBot V11), WeCom Bot, API Service (OpenAI-compatible), **Gateway multi-channel concurrent mode**
- 📊 **Dashboard**: Built-in Web UI for real-time monitoring of token usage, session history, and tool call traces
- 🧩 **Skills System**: Dynamically load Skills from workspace
- 📢 **Notification Push**: WeCom group bot and Webhook notifications

![qq bot](./Documentation/images/qq_bot.gif)

<div align="center">QQ bot mode</div>

![cli](./Documentation/images/cli.gif)

<div align="center">CLI mode</div>

![chatbox](./Documentation/images/chatbox.gif)

<div align="center">In API mode, you can use ChatBox to communicate with DotBot</div>

![dashboard](./Documentation/images/dashboard.png)

<div align="center">Dashboard monitors usage and session history</div>

## 🚀 Quick Start

```bash
# Build the Release package
build.bat

# Configure the path to environment variables
cd Release/DotBot
bash install_to_path.ps1

# Enter the workspace
cd Workspace

# Start DotBot
dotbot
```

## 📚 Documentation

| Document | Description |
|----------|-------------|
| [Configuration Guide](./Documentation/en/config_guide.md) | Tools, security, blacklists, approval, MCP, Gateway |
| [API Mode Guide](./Documentation/en/api_guide.md) | OpenAI-compatible API, tool filtering, SDK examples |
| [QQ Bot Guide](./Documentation/en/qq_bot_guide.md) | NapCat / permissions / approval |
| [WeCom Guide](./Documentation/en/wecom_guide.md) | WeCom push notifications / bot mode |
| [DashBoard Guide](./Documentation/en/dash_board_guide.md) | Built-in Web debugging UI, Trace data viewer |
| [Documentation Index](./Documentation/en/index.md) | Full documentation navigation |

## 🙏 Credits

Inspired by nanobot and built on the Microsoft Agent Framework, this project utilized multiple AI tools to complete all development work for the first release version within two weeks.

Google Nano Banana Pro generated the project's logo.

Thanks to [Devin AI](https://devin.ai/) for providing free ACU credits to facilitate development.

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [NapNeko/NapCatQQ](https://github.com/NapNeko/NapCatQQ)
- [spectreconsole/spectre.console](https://github.com/spectreconsole/spectre.console)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)

## 📄 License

Apache License 2.0

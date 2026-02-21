<div align="center">

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/AkiKurisu/DotBot)
[![Zhihu](https://img.shields.io/badge/知乎-AkiKurisu-0084ff?style=flat-square)](https://www.zhihu.com/people/akikurisu)
[![Bilibili](https://img.shields.io/badge/Bilibili-爱姬Kurisu-00A1D6?style=flat-square)](https://space.bilibili.com/20472331)

**中文 | [English](./README.md)**

# DotBot

**DotBot** (.Bot) 是一个 C# 编写的轻量级 OpenClaw，安全可靠，开箱即用。

![banner](./Documentation/images/banner.png)

</div>

## ✨ 主要特性

<table>
<tr>
<td width="33%" align="center"><b>🪶 轻量极简</b><br/>C# 编写，基于 .NET 10 构建，单文件，无复杂依赖。</td>
<td width="33%" align="center"><b>🚀 一键部署</b><br/>无需复杂的配置流程。</td>
<td width="33%" align="center"><b>🔒 安全审批</b><br/>多层安全防护+审批流程，高危操作可控。</td>
</tr>
</table>

- 🛠️ **工具能力**: 文件读写（工作区内）、受控 Shell 命令、Web 抓取、可选子智能体（SubAgent）
- 🔌 **MCP 接入**: 支持通过 [Model Context Protocol](https://modelcontextprotocol.io/) 接入外部工具服务
- 🎯 **运行形态**: 本地 REPL、QQ 机器人（OneBot V11）、企业微信机器人、API 服务（OpenAI 兼容）、**Gateway 多 Channel 并发模式**
- 📊 **监控面板**: 内置 Web 调试界面，实时监控 Token 使用、会话历史和工具调用追踪
- 🧩 **技能系统**: 支持动态加载技能
- 📢 **通知推送**: 企业微信群机器人和 Webhook 推送

![qq bot](./Documentation/images/qq_bot.gif)

<div align="center">QQ 机器人模式</div>

![cli](./Documentation/images/cli.gif)

<div align="center">CLI 模式</div>

![chatbox](./Documentation/images/chatbox.gif)

<div align="center">API 模式下可以使用 ChatBox 来和 DotBot 对话</div>

![dashboard](./Documentation/images/dashboard.png)

<div align="center">DashBoard 监控用量和会话历史</div>

## 🚀 快速开始

```bash
# 构建 Release 包
build.bat

# 配置路径到环境变量
cd Release/DotBot
bash install_to_path.ps1

# 进入工作区
cd Workspace

# 启动 DotBot
dotbot
```

## 📚 文档导航

| 文档 | 说明 |
|------|------|
| [架构与安全](./Documentation/architecture.md) | 架构设计、安全模型 |
| [配置指南](./Documentation/config_guide.md) | 工具、安全、黑名单、审批、MCP、Gateway |
| [API 模式指南](./Documentation/api_guide.md) | OpenAI 兼容 API、工具过滤、SDK 示例 |
| [QQ 机器人指南](./Documentation/qq_bot_guide.md) | NapCat/权限/审批 |
| [企业微信指南](./Documentation/wecom_guide.md) | 企业微信推送/机器人模式 |
| [DashBoard 指南](./Documentation/dash_board_guide.md) | 内置 Web 调试界面、追踪数据查看器 |
| [文档索引](./Documentation/index.md) | 完整文档导航 |

## 🙏 致谢

本项目受 nanobot 启发，基于微软 Agent Framework 打造，使用多个 AI 工具在两周内完成第一个 Release 版本的所有开发内容。

谷歌 Nano Banana Pro 生成了本项目的 Logo。

感谢 [Devin AI](https://devin.ai/) 提供了免费的 ACU 额度为开发提供便捷。

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [NapNeko/NapCatQQ](https://github.com/NapNeko/NapCatQQ)
- [spectreconsole/spectre.console](https://github.com/spectreconsole/spectre.console)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)

## 📄 许可证

Apache License 2.0

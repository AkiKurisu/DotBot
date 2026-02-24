---
description: "Browser automation via Playwright MCP - navigate, click, fill forms, take screenshots, and inspect web pages."
bins: npx
---

# Browser Automation (Playwright MCP)

You have access to browser automation tools provided by the Playwright MCP server.
These tools let you control a real browser (Chromium) to interact with web pages.

## Setup

> **If no `browser_*` tools are available**, the Playwright MCP server is not configured.
> Tell the user to add the following to their `appsettings.json` and restart DotBot:

This skill requires the Playwright MCP server to be configured in `appsettings.json`:

```json
{
  "McpServers": [
    {
      "Name": "playwright",
      "Transport": "stdio",
      "Command": "npx",
      "Arguments": ["-y", "@playwright/mcp@latest"]
    }
  ]
}
```  

Prerequisites:
- Node.js 18+
- Chromium browser: `npx playwright install chromium`

### If Browser Installation Fails

If `browser_install` fails (e.g. due to permission errors or network restrictions), tell the user to install Chromium manually with administrator privileges:

1. Open **PowerShell as Administrator** (right-click the Start menu → "Windows PowerShell (Admin)" or "Terminal (Admin)")
2. Run:
   ```powershell
   npx playwright install chromium
   ```
3. Wait for the download to complete, then retry the task.
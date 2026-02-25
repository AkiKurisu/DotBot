---
description: "Browser automation via Playwright MCP - navigate, click, fill forms, take screenshots, and inspect web pages."
bins: npx
---

# Browser Automation (Playwright MCP)

You have access to browser automation tools provided by the Playwright MCP server.
These tools let you control a real browser to interact with web pages.

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
- Chrome browser: `npx playwright install chrome`

### If Browser Installation Fails

If `browser_install` fails (e.g. due to permission errors or network restrictions), tell the user to install Chrome manually with administrator privileges:

1. Open **PowerShell as Administrator** (right-click the Start menu → "Windows PowerShell (Admin)" or "Terminal (Admin)")
2. Run:
   ```powershell
   npx playwright install chrome
   ```
3. Wait for the download to complete, then retry the task.

### If the Browser Is Not Supported on the User's OS

Playwright supports multiple browsers: **Chrome**, **Firefox**, and **WebKit**. If one browser fails to install or run on the user's OS (e.g. WebKit is not supported on Windows), try a different one.

**Step 1 — Install an alternative browser:**

```powershell
# Try Firefox
npx playwright install firefox

# Or try WebKit (macOS / Linux only)
npx playwright install webkit
```

**Step 2 — Update `appsettings.json` to specify the browser** using the `--browser` argument:

```json
{
  "McpServers": [
    {
      "Name": "playwright",
      "Transport": "stdio",
      "Command": "npx",
      "Arguments": ["-y", "@playwright/mcp@latest", "--browser", "firefox"]
    }
  ]
}
```

Supported values for `--browser`: `firefox`, `webkit`, `chrome`, `msedge`.

> **Tip:** On Linux servers, stick to `chrome` or `firefox` (headless). WebKit is only reliable on macOS.
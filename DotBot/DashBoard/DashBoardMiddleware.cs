using System.Text.Encodings.Web;
using System.Text.Json;
using DotBot.CLI;
using DotBot.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DotBot.DashBoard;

public static class DashBoardMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void MapDashBoard(this IEndpointRouteBuilder endpoints, TraceStore traceStore, AppConfig config, TokenUsageStore? tokenUsageStore = null)
    {
        endpoints.MapGet("/dashboard/", ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            return ctx.Response.WriteAsync(DashBoardFrontend.GetHtml());
        });

        endpoints.MapGet("/dashboard/api/summary", () =>
        {
            var summary = traceStore.GetSummary();
            return Results.Json(summary, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions", () =>
        {
            var sessions = traceStore.GetSessions();
            var result = sessions.Select(s => new
            {
                s.SessionKey,
                startedAt = s.StartedAt.ToString("o"),
                lastActivityAt = s.LastActivityAt.ToString("o"),
                s.TotalInputTokens,
                s.TotalOutputTokens,
                totalTokens = s.TotalInputTokens + s.TotalOutputTokens,
                s.RequestCount,
                s.ResponseCount,
                s.ToolCallCount,
                s.ErrorCount,
                s.ContextCompactionCount,
                totalToolDurationMs = s.TotalToolDurationMs,
                avgToolDurationMs = s.AvgToolDurationMs,
                maxToolDurationMs = s.MaxToolDurationMs,
                finalSystemPrompt = s.FinalSystemPrompt,
                toolNames = s.ToolNames
            });
            return Results.Json(result, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions/{sessionKey}/events", (string sessionKey) =>
        {
            var events = traceStore.GetEvents(sessionKey);
            return Results.Json(events, JsonOptions);
        });

        endpoints.MapDelete("/dashboard/api/sessions/{sessionKey}", (string sessionKey) =>
        {
            var deleted = traceStore.ClearSession(sessionKey);
            return deleted
                ? Results.Json(new { deleted = true, sessionKey }, JsonOptions)
                : Results.Json(new { deleted = false, sessionKey }, JsonOptions, statusCode: 404);
        });

        endpoints.MapDelete("/dashboard/api/sessions", () =>
        {
            traceStore.ClearAll();
            return Results.Json(new { cleared = true }, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/tools", () =>
        {
            var icons = ToolIconRegistry.GetAllToolIcons();
            var tools = icons.Select(kv => new { name = kv.Key, icon = kv.Value });
            return Results.Json(new { tools }, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/config", () => Results.Json(new
        {
            // Core settings
            model = config.Model,
            endPoint = config.EndPoint,
            systemInstructions = config.SystemInstructions,
            maxToolCallRounds = config.MaxToolCallRounds,
            subagentMaxToolCallRounds = config.SubagentMaxToolCallRounds,
            subagentMaxConcurrency = config.SubagentMaxConcurrency,
            compactSessions = config.CompactSessions,
            maxContextTokens = config.MaxContextTokens,
            debugMode = config.DebugMode,
            // Tools config
            tools = new
            {
                file = new
                {
                    requireApprovalOutsideWorkspace = config.Tools.File.RequireApprovalOutsideWorkspace,
                    maxFileSize = config.Tools.File.MaxFileSize
                },
                shell = new
                {
                    requireApprovalOutsideWorkspace = config.Tools.Shell.RequireApprovalOutsideWorkspace,
                    timeout = config.Tools.Shell.Timeout,
                    maxOutputLength = config.Tools.Shell.MaxOutputLength
                },
                web = new
                {
                    maxChars = config.Tools.Web.MaxChars,
                    timeout = config.Tools.Web.Timeout,
                    searchMaxResults = config.Tools.Web.SearchMaxResults,
                    searchProvider = config.Tools.Web.SearchProvider
                }
            },
            // QQ Bot config
            qqBot = new
            {
                enabled = config.QQBot.Enabled,
                host = config.QQBot.Host,
                port = config.QQBot.Port,
                accessToken = string.IsNullOrEmpty(config.QQBot.AccessToken) ? "(not set)" : "***",
                adminUsers = config.QQBot.AdminUsers.Count > 0 ? (object)config.QQBot.AdminUsers : "(none)",
                whitelistedUsers = config.QQBot.WhitelistedUsers.Count > 0 ? (object)config.QQBot.WhitelistedUsers : "(none)",
                whitelistedGroups = config.QQBot.WhitelistedGroups.Count > 0 ? (object)config.QQBot.WhitelistedGroups : "(none)",
                approvalTimeoutSeconds = config.QQBot.ApprovalTimeoutSeconds
            },
            // Security config
            security = new
            {
                blacklistedPaths = config.Security.BlacklistedPaths.Count > 0 ? (object)config.Security.BlacklistedPaths : "(none)"
            },
            // Heartbeat config
            heartbeat = new
            {
                enabled = config.Heartbeat.Enabled,
                intervalSeconds = config.Heartbeat.IntervalSeconds,
                notifyAdmin = config.Heartbeat.NotifyAdmin
            },
            // WeCom config
            weCom = new
            {
                enabled = config.WeCom.Enabled,
                webhookUrl = string.IsNullOrEmpty(config.WeCom.WebhookUrl) ? "(not set)" : "***"
            },
            // WeCom Bot config
            weComBot = new
            {
                enabled = config.WeComBot.Enabled,
                host = config.WeComBot.Host,
                port = config.WeComBot.Port,
                adminUsers = config.WeComBot.AdminUsers.Count > 0 ? (object)config.WeComBot.AdminUsers : "(none)",
                whitelistedUsers = config.WeComBot.WhitelistedUsers.Count > 0 ? (object)config.WeComBot.WhitelistedUsers : "(none)",
                whitelistedChats = config.WeComBot.WhitelistedChats.Count > 0 ? (object)config.WeComBot.WhitelistedChats : "(none)",
                approvalTimeoutSeconds = config.WeComBot.ApprovalTimeoutSeconds
            },
            // Cron config
            cron = new
            {
                enabled = config.Cron.Enabled,
                storePath = config.Cron.StorePath
            },
            // API config
            api = new
            {
                enabled = config.Api.Enabled,
                host = config.Api.Host,
                port = config.Api.Port,
                apiKey = string.IsNullOrEmpty(config.Api.ApiKey) ? "(not set)" : "***",
                autoApprove = config.Api.AutoApprove,
                approvalMode = config.Api.ApprovalMode,
                approvalTimeoutSeconds = config.Api.ApprovalTimeoutSeconds
            },
            enabledTools = config.EnabledTools.Count > 0
                ? (object)config.EnabledTools
                : "(all)",
            enabledToolsSource = config.EnabledTools.Count > 0
                ? "global"
                : "all",
            // Dashboard config
            dashBoard = new
            {
                enabled = config.DashBoard.Enabled,
                host = config.DashBoard.Host,
                port = config.DashBoard.Port
            },
            // MCP servers
            mcpServers = config.McpServers.Select(m => new
            {
                name = m.Name,
                transport = m.Transport,
                enabled = m.Enabled
            })
        }, JsonOptions));

        if (tokenUsageStore != null)
        {
            endpoints.MapGet("/dashboard/api/token-usage/summary", () =>
            {
                var summary = tokenUsageStore.GetSummary();
                return Results.Json(summary, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/token-usage/qq/private", () =>
            {
                var users = tokenUsageStore.GetQQPrivateUsers();
                var result = users.Select(u => new
                {
                    userId = u.UserId,
                    displayName = u.DisplayName,
                    totalInputTokens = u.TotalInputTokens,
                    totalOutputTokens = u.TotalOutputTokens,
                    totalTokens = u.TotalTokens,
                    requestCount = u.RequestCount,
                    lastActiveAt = u.LastActiveAt.ToString("o")
                });
                return Results.Json(result, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/token-usage/qq/groups", () =>
            {
                var groups = tokenUsageStore.GetQQGroups();
                var result = groups.Select(g => new
                {
                    groupId = g.GroupId,
                    groupName = g.GroupName,
                    totalInputTokens = g.TotalInputTokens,
                    totalOutputTokens = g.TotalOutputTokens,
                    totalTokens = g.TotalTokens,
                    totalRequestCount = g.TotalRequestCount,
                    lastActiveAt = g.LastActiveAt.ToString("o"),
                    users = g.Users.Values
                        .OrderByDescending(u => u.TotalTokens)
                        .Select(u => new
                        {
                            userId = u.UserId,
                            displayName = u.DisplayName,
                            totalInputTokens = u.TotalInputTokens,
                            totalOutputTokens = u.TotalOutputTokens,
                            totalTokens = u.TotalTokens,
                            requestCount = u.RequestCount,
                            lastActiveAt = u.LastActiveAt.ToString("o")
                        })
                });
                return Results.Json(result, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/token-usage/wecom", () =>
            {
                var users = tokenUsageStore.GetWeComUsers();
                var result = users.Select(u => new
                {
                    userId = u.UserId,
                    displayName = u.DisplayName,
                    totalInputTokens = u.TotalInputTokens,
                    totalOutputTokens = u.TotalOutputTokens,
                    totalTokens = u.TotalTokens,
                    requestCount = u.RequestCount,
                    lastActiveAt = u.LastActiveAt.ToString("o")
                });
                return Results.Json(result, JsonOptions);
            });
        }

        endpoints.MapGet("/dashboard/api/events/stream", async ctx =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var cancellationToken = ctx.RequestAborted;
            var reader = traceStore.SseReader;

            try
            {
                await foreach (var evt in reader.ReadAllAsync(cancellationToken))
                {
                    var json = JsonSerializer.Serialize(evt, JsonOptions);
                    await ctx.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await ctx.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
            }
        });
    }
}

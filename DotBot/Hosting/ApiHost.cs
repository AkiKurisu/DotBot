using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DotBot.Agents;
using DotBot.CLI;
using DotBot.Context;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Heartbeat;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.Security;
using DotBot.Skills;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace DotBot.Hosting;

public sealed class ApiHost(
    IServiceProvider sp,
    AppConfig config,
    DotBotPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    CronService cronService,
    McpClientManager mcpClientManager) : IDotBotHost
{
    private AgentFactory _agentFactory = null!;
    
    private ApiApprovalService _approvalService = null!;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var traceStore = sp.GetService<TraceStore>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var approvalMode = ApiApprovalService.ParseMode(config.Api.ApprovalMode, config.Api.AutoApprove);
        _approvalService = new ApiApprovalService(approvalMode, config.Api.ApprovalTimeoutSeconds);

        _agentFactory = new AgentFactory(
            paths.BotPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, _approvalService, blacklist,
            cronTools: cronTools,
            mcpClientManager: mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
            traceCollector: traceCollector);

        var tools = _agentFactory.CreateDefaultTools();
        
        var builder = WebApplication.CreateBuilder();
        builder.AddOpenAIChatCompletions();
        
        var agentBuilder = builder.AddAIAgent(
            "dotbot",
            _agentFactory.CreateTracingChatClient(traceCollector),
            CreateApiAgentOptions(tools, traceCollector))
            .WithAITools(tools.ToArray());

        var app = builder.Build();

        if (!string.IsNullOrEmpty(config.Api.ApiKey))
        {
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? "";
                if (path.StartsWith("/dotbot/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
                {
                    if (path == "/v1/health")
                    {
                        await next();
                        return;
                    }

                    var authHeader = context.Request.Headers.Authorization.ToString();
                    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                        authHeader["Bearer ".Length..].Trim() != config.Api.ApiKey)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" }, cancellationToken: cancellationToken);
                        return;
                    }
                }

                await next();
            });
        }

        if (traceCollector != null)
        {
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? "";
                if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                {
                    var sessionKey = context.Request.Headers["X-Session-Key"].FirstOrDefault()
                                     ?? $"api:{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
                    TracingChatClient.CurrentSessionKey = sessionKey;
                    TracingChatClient.ResetCallState(sessionKey);
                    try
                    {
                        await next();
                    }
                    finally
                    {
                        TracingChatClient.ResetCallState(sessionKey);
                        TracingChatClient.CurrentSessionKey = null;
                    }
                }
                else
                {
                    await next();
                }
            });
        }

        app.Use(NonStreamingResponseMiddleware);

        app.MapOpenAIChatCompletions(agentBuilder);

        MapAdditionalRoutes(app);

        var agent = _agentFactory.CreateAgentWithTools(tools);
        var runner = new AgentRunner(agent, sessionStore, _agentFactory, traceCollector);

        if (config.DashBoard.Enabled && traceStore != null)
        {
            app.MapDashBoard(traceStore, config, tokenUsageStore);
            AnsiConsole.MarkupLine($"[green]DashBoard available at[/] [link]http://{config.Api.Host}:{config.Api.Port}/dashboard[/]");
        }

        cronService.OnJob = async job =>
        {
            var sessionKey = $"cron:{job.Id}";
            await runner.RunAsync(job.Payload.Message, sessionKey);
        };

        using var heartbeatService = new HeartbeatService(
            paths.BotPath,
            onHeartbeat: runner.RunAsync,
            intervalSeconds: config.Heartbeat.IntervalSeconds,
            enabled: config.Heartbeat.Enabled);

        if (config.Heartbeat.Enabled)
        {
            heartbeatService.Start();
            AnsiConsole.MarkupLine($"[green]Heartbeat started (interval: {config.Heartbeat.IntervalSeconds}s)[/]");
        }
        if (config.Cron.Enabled)
        {
            cronService.Start();
            AnsiConsole.MarkupLine($"[green]Cron service started ({cronService.ListJobs().Count} jobs)[/]");
        }

        var url = $"http://{config.Api.Host}:{config.Api.Port}";
        AnsiConsole.MarkupLine($"[green]DotBot API listening on {Markup.Escape(url)}[/]");
        AnsiConsole.MarkupLine("[grey]Endpoints (OpenAI-compatible):[/]");
        AnsiConsole.MarkupLine("[grey]  POST /dotbot/v1/chat/completions[/]");
        AnsiConsole.MarkupLine("[grey]Additional endpoints:[/]");
        AnsiConsole.MarkupLine("[grey]  GET  /v1/health[/]");
        AnsiConsole.MarkupLine("[grey]  GET  /v1/tools[/]");
        AnsiConsole.MarkupLine("[grey]  GET  /v1/sessions[/]");
        AnsiConsole.MarkupLine("[grey]  DELETE /v1/sessions/{id}[/]");

        if (approvalMode == ApiApprovalMode.Interactive)
        {
            AnsiConsole.MarkupLine("[grey]  GET  /v1/approvals[/]");
            AnsiConsole.MarkupLine("[grey]  POST /v1/approvals/{id}[/]");
            AnsiConsole.MarkupLine($"[green]Approval mode: interactive (timeout: {config.Api.ApprovalTimeoutSeconds}s)[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[grey]Approval mode: {approvalMode.ToString().ToLowerInvariant()}[/]");
        }

        if (config.EnabledTools.Count > 0)
        {
            AnsiConsole.MarkupLine($"[grey]Globally enabled tools: {string.Join(", ", config.EnabledTools)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[grey]All tools enabled ({tools.Count} tools)[/]");
        }

        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop...[/]");

        _ = app.RunAsync(url);
        await WaitForShutdownSignalAsync(cancellationToken);

        AnsiConsole.MarkupLine("[yellow]Shutting down API server...[/]");
        heartbeatService.Stop();
        cronService.Stop();
        await app.StopAsync(cancellationToken);
    }

    private void MapAdditionalRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/health", () => Results.Json(new
        {
            status = "ok",
            version = "1.0.0",
            mode = "api",
            model = config.Model,
            protocol = "openai-compatible"
        }));

        endpoints.MapGet("/v1/tools", (HttpContext context) =>
        {
            if (!Authenticate(context))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var tools = _agentFactory.LastCreatedTools ?? [];
            var toolList = tools.Select(t => new
            {
                name = t.Name,
                icon = ToolIconRegistry.GetToolIcon(t.Name)
            }).ToList();

            return Results.Json(new { tools = toolList });
        });

        endpoints.MapGet("/v1/sessions", (HttpContext context) =>
        {
            if (!Authenticate(context))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var sessions = sessionStore.ListSessions();
            return Results.Json(new { sessions });
        });

        endpoints.MapDelete("/v1/sessions/{id}", (HttpContext context, string id) =>
        {
            if (!Authenticate(context))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var deleted = sessionStore.Delete(id);
            if (deleted)
                _agentFactory.RemoveTokenTracker(id);

            return deleted
                ? Results.Json(new { deleted = true, id })
                : Results.Json(new { deleted = false, id }, statusCode: StatusCodes.Status404NotFound);
        });

        endpoints.MapGet("/v1/approvals", (HttpContext context) =>
        {
            if (!Authenticate(context))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            var pending = _approvalService.PendingApprovals;
            var list = pending.Select(a => new
            {
                id = a.Id,
                type = a.Type,
                operation = a.Operation,
                detail = a.Detail,
                createdAt = a.CreatedAt.ToString("o")
            }).ToList();

            return Results.Json(new { approvals = list });
        });

        endpoints.MapPost("/v1/approvals/{id}", async (HttpContext context, string id) =>
        {
            if (!Authenticate(context))
                return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);

            ApprovalDecision? body;
            try
            {
                body = await context.Request.ReadFromJsonAsync<ApprovalDecision>();
            }
            catch
            {
                return Results.Json(new { error = "invalid request body, expected {\"approved\": true/false}" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (body == null)
                return Results.Json(new { error = "missing request body" },
                    statusCode: StatusCodes.Status400BadRequest);

            var resolved = _approvalService.Resolve(id, body.Approved);
            if (!resolved)
                return Results.Json(new { error = "approval not found or already resolved" },
                    statusCode: StatusCodes.Status404NotFound);

            return Results.Json(new { id, approved = body.Approved });
        });
    }

    private ChatClientAgentOptions CreateApiAgentOptions(IReadOnlyList<AITool> tools, TraceCollector? traceCollector)
    {
        return new ChatClientAgentOptions
        {
            AIContextProviderFactory = (_, _) => new ValueTask<AIContextProvider>(
                new MemoryContextProvider(
                    memoryStore,
                    skillsLoader,
                    paths.BotPath,
                    paths.WorkspacePath,
                    config.SystemInstructions,
                    traceCollector,
                    () => tools.Select(t => t.Name).ToArray()))
        };
    }
    
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
    private sealed class ApprovalDecision
    {
        public bool Approved { get; set; }
    }

    private bool Authenticate(HttpContext context)
    {
        var apiKey = config.Api.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
            return true;

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim() == apiKey;
        }

        return false;
    }

    private static async Task NonStreamingResponseMiddleware(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var isStreaming = false;
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (doc.RootElement.TryGetProperty("stream", out var streamProp) &&
                streamProp.ValueKind == JsonValueKind.True)
                isStreaming = true;
        }
        catch
        {
            // ignored
        }

        if (isStreaming)
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next(context);

        buffer.Position = 0;
        var contentType = context.Response.ContentType ?? "";
        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(buffer);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                {
                    var candidateChoices = new List<JsonElement>();
                    JsonElement? bestTextChoice = null;

                    for (var i = 0; i < choices.GetArrayLength(); i++)
                    {
                        var choice = choices[i];

                        var hasToolCalls = false;
                        var hasNonEmptyContent = false;

                        if (choice.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
                        {
                            if (msg.TryGetProperty("tool_calls", out var toolCalls) &&
                                toolCalls.ValueKind == JsonValueKind.Array &&
                                toolCalls.GetArrayLength() > 0)
                            {
                                hasToolCalls = true;
                            }

                            if (msg.TryGetProperty("content", out var content) &&
                                content.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(content.GetString()))
                            {
                                hasNonEmptyContent = true;
                                bestTextChoice = choice;
                            }
                        }

                        // Drop pure internal tool-call choices to avoid client-side parser errors.
                        if (hasToolCalls && !hasNonEmptyContent)
                            continue;

                        candidateChoices.Add(choice);
                    }

                    if (candidateChoices.Count > 0)
                    {
                        JsonElement selectedChoice;
                        if (candidateChoices.Count == 1)
                        {
                            selectedChoice = candidateChoices[0];
                        }
                        else
                        {
                            // Keep compatibility with clients that expect a single text-first choice.
                            selectedChoice = bestTextChoice ?? candidateChoices[^1];
                        }

                        using var output = new MemoryStream();
                        using (var writer = new Utf8JsonWriter(output))
                        {
                            writer.WriteStartObject();
                            foreach (var prop in root.EnumerateObject())
                            {
                                if (prop.Name == "choices")
                                {
                                    writer.WriteStartArray("choices");
                                    writer.WriteStartObject();
                                    foreach (var cp in selectedChoice.EnumerateObject())
                                    {
                                        if (cp.Name == "index")
                                        {
                                            writer.WriteNumber("index", 0);
                                        }
                                        else if (cp.Name == "message" && cp.Value.ValueKind == JsonValueKind.Object)
                                        {
                                            writer.WriteStartObject("message");
                                            foreach (var mp in cp.Value.EnumerateObject())
                                            {
                                                if (!string.Equals(mp.Name, "tool_calls", StringComparison.Ordinal))
                                                {
                                                    mp.WriteTo(writer);
                                                }
                                            }
                                            writer.WriteEndObject();
                                        }
                                        else
                                        {
                                            cp.WriteTo(writer);
                                        }
                                    }
                                    writer.WriteEndObject();
                                    writer.WriteEndArray();
                                }
                                else
                                {
                                    prop.WriteTo(writer);
                                }
                            }
                            writer.WriteEndObject();
                        }

                        context.Response.Body = originalBody;
                        context.Response.ContentLength = output.Length;
                        output.Position = 0;
                        await output.CopyToAsync(originalBody);
                        return;
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        buffer.Position = 0;
        context.Response.Body = originalBody;
        context.Response.ContentLength = buffer.Length;
        await buffer.CopyToAsync(originalBody);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private static async Task WaitForShutdownSignalAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult();
        };
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;
    }
}

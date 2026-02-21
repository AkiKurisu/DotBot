using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DotBot.Abstractions;
using DotBot.Agents;
using DotBot.Api;
using DotBot.CLI;
using DotBot.Configuration;
using DotBot.Context;
using DotBot.Cron;
using DotBot.DashBoard;
using DotBot.Hosting;
using DotBot.Mcp;
using DotBot.Memory;
using DotBot.Security;
using DotBot.Skills;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using Spectre.Console;

namespace DotBot.Gateway;

/// <summary>
/// Gateway channel service for OpenAI-compatible HTTP API.
/// Manages the ASP.NET Core web server and agent lifecycle as part of a multi-channel gateway.
/// </summary>
public sealed class ApiChannelService : IChannelService
{
    private readonly IServiceProvider _sp;
    private readonly AppConfig _config;
    private readonly DotBotPaths _paths;
    private readonly SessionStore _sessionStore;
    private readonly MemoryStore _memoryStore;
    private readonly SkillsLoader _skillsLoader;
    private readonly PathBlacklist _blacklist;
    private readonly McpClientManager _mcpClientManager;
    private readonly ApiApprovalService _approvalService;

    private WebApplication? _webApp;
    private AgentFactory? _agentFactory;

    public string Name => "api";

    /// <summary>
    /// Optional callback invoked by GatewayHost to inject additional routes (e.g. dashboard)
    /// onto this channel's WebApplication before it starts listening.
    /// </summary>
    public Action<WebApplication>? OnConfigureApp { get; set; }

    public ApiChannelService(
        IServiceProvider sp,
        AppConfig config,
        DotBotPaths paths,
        SessionStore sessionStore,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        PathBlacklist blacklist,
        McpClientManager mcpClientManager,
        ApiApprovalService approvalService)
    {
        _sp = sp;
        _config = config;
        _paths = paths;
        _sessionStore = sessionStore;
        _memoryStore = memoryStore;
        _skillsLoader = skillsLoader;
        _blacklist = blacklist;
        _mcpClientManager = mcpClientManager;
        _approvalService = approvalService;
    }

    private AgentFactory BuildAgentFactory()
    {
        var cronTools = _sp.GetService<CronTools>();
        var traceCollector = _sp.GetService<TraceCollector>();

        return new AgentFactory(
            _paths.BotPath, _paths.WorkspacePath, _config,
            _memoryStore, _skillsLoader, _approvalService, _blacklist,
            toolProviders: null,
            toolProviderContext: new ToolProviderContext
            {
                Config = _config,
                ChatClient = new OpenAIClient(
                    new ApiKeyCredential(_config.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(_config.EndPoint) })
                    .GetChatClient(_config.Model),
                WorkspacePath = _paths.WorkspacePath,
                BotPath = _paths.BotPath,
                MemoryStore = _memoryStore,
                SkillsLoader = _skillsLoader,
                ApprovalService = _approvalService,
                PathBlacklist = _blacklist,
                CronTools = cronTools,
                McpClientManager = _mcpClientManager.Tools.Count > 0 ? _mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _agentFactory = BuildAgentFactory();
        var traceCollector = _sp.GetService<TraceCollector>();

        var tools = _agentFactory.CreateDefaultTools();

        var builder = WebApplication.CreateBuilder();
        builder.AddOpenAIChatCompletions();

        var agentBuilder = builder.AddAIAgent(
            "dotbot",
            _agentFactory.CreateTracingChatClient(traceCollector),
            CreateApiAgentOptions(tools, traceCollector))
            .WithAITools(tools.ToArray());

        _webApp = builder.Build();

        if (!string.IsNullOrEmpty(_config.Api.ApiKey))
        {
            _webApp.Use(async (context, next) =>
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
                        authHeader["Bearer ".Length..].Trim() != _config.Api.ApiKey)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" },
                            cancellationToken: cancellationToken);
                        return;
                    }
                }
                await next();
            });
        }

        if (traceCollector != null)
        {
            _webApp.Use(async (context, next) =>
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

        _webApp.Use(NonStreamingResponseMiddleware);
        _webApp.MapOpenAIChatCompletions(agentBuilder);

        var agent = _agentFactory.CreateAgentWithTools(tools);
        var runner = new AgentRunner(agent, _sessionStore, _agentFactory, traceCollector);

        MapAdditionalRoutes(_webApp, runner);

        // Allow GatewayHost to inject additional routes (e.g. dashboard) before the app starts.
        OnConfigureApp?.Invoke(_webApp);

        var url = $"http://{_config.Api.Host}:{_config.Api.Port}";
        AnsiConsole.MarkupLine($"[green][[Gateway]][/] API listening on {Markup.Escape(url)}");

        var approvalMode = ApiApprovalService.ParseMode(_config.Api.ApprovalMode, _config.Api.AutoApprove);
        AnsiConsole.MarkupLine(
            $"[grey]  Approval mode: {approvalMode.ToString().ToLowerInvariant()}[/]");

        _ = _webApp.RunAsync(url);

        // Wait for cancellation
        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
    }

    public async Task StopAsync()
    {
        if (_webApp != null)
            await _webApp.StopAsync();
    }

    public Task DeliverMessageAsync(string target, string content)
    {
        // API channel has no proactive message delivery capability
        return Task.CompletedTask;
    }

    private void MapAdditionalRoutes(IEndpointRouteBuilder endpoints, AgentRunner runner)
    {
        endpoints.MapGet("/v1/health", () => Results.Json(new
        {
            status = "ok",
            version = "1.0.0",
            mode = "gateway-api",
            model = _config.Model,
            protocol = "openai-compatible"
        }));

        endpoints.MapGet("/v1/tools", (HttpContext context) =>
        {
            if (!Authenticate(context))
                return Results.Json(new { error = "unauthorized" },
                    statusCode: StatusCodes.Status401Unauthorized);

            var toolList = (_agentFactory?.LastCreatedTools ?? []).Select(t => new
            {
                name = t.Name,
                icon = ToolIconRegistry.GetToolIcon(t.Name)
            }).ToList();

            return Results.Json(new { tools = toolList });
        });

        endpoints.MapGet("/v1/sessions", (HttpContext context) =>
        {
            if (!Authenticate(context))
                return Results.Json(new { error = "unauthorized" },
                    statusCode: StatusCodes.Status401Unauthorized);

            return Results.Json(new { sessions = _sessionStore.ListSessions() });
        });

        endpoints.MapDelete("/v1/sessions/{id}", (HttpContext context, string id) =>
        {
            if (!Authenticate(context))
                return Results.Json(new { error = "unauthorized" },
                    statusCode: StatusCodes.Status401Unauthorized);

            var deleted = _sessionStore.Delete(id);
            if (deleted) _agentFactory?.RemoveTokenTracker(id);

            return deleted
                ? Results.Json(new { deleted = true, id })
                : Results.Json(new { deleted = false, id },
                    statusCode: StatusCodes.Status404NotFound);
        });

        endpoints.MapGet("/v1/approvals", (HttpContext context) =>
        {
            if (!Authenticate(context))
                return Results.Json(new { error = "unauthorized" },
                    statusCode: StatusCodes.Status401Unauthorized);

            var list = _approvalService.PendingApprovals.Select(a => new
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
                return Results.Json(new { error = "unauthorized" },
                    statusCode: StatusCodes.Status401Unauthorized);

            ApprovalDecision? body;
            try
            {
                body = await context.Request.ReadFromJsonAsync<ApprovalDecision>();
            }
            catch
            {
                return Results.Json(
                    new { error = "invalid request body, expected {\"approved\": true/false}" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (body == null)
                return Results.Json(new { error = "missing request body" },
                    statusCode: StatusCodes.Status400BadRequest);

            var resolved = _approvalService.Resolve(id, body.Approved);
            if (!resolved)
                return Results.Json(
                    new { error = "approval not found or already resolved" },
                    statusCode: StatusCodes.Status404NotFound);

            return Results.Json(new { id, approved = body.Approved });
        });
    }

    private ChatClientAgentOptions CreateApiAgentOptions(IReadOnlyList<AITool> tools,
        TraceCollector? traceCollector)
    {
        return new ChatClientAgentOptions
        {
            AIContextProviderFactory = (_, _) => new ValueTask<AIContextProvider>(
                new MemoryContextProvider(
                    _memoryStore, _skillsLoader,
                    _paths.BotPath, _paths.WorkspacePath,
                    _config.SystemInstructions, traceCollector,
                    () => tools.Select(t => t.Name).ToArray()))
        };
    }

    private bool Authenticate(HttpContext context)
    {
        var apiKey = _config.Api.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
            return true;

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authHeader["Bearer ".Length..].Trim() == apiKey;

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
        catch { /* ignored */ }

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

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array)
                {
                    var candidateChoices = new List<JsonElement>();
                    JsonElement? bestTextChoice = null;

                    for (var i = 0; i < choices.GetArrayLength(); i++)
                    {
                        var choice = choices[i];
                        var hasToolCalls = false;
                        var hasNonEmptyContent = false;

                        if (choice.TryGetProperty("message", out var msg) &&
                            msg.ValueKind == JsonValueKind.Object)
                        {
                            if (msg.TryGetProperty("tool_calls", out var toolCalls) &&
                                toolCalls.ValueKind == JsonValueKind.Array &&
                                toolCalls.GetArrayLength() > 0)
                                hasToolCalls = true;

                            if (msg.TryGetProperty("content", out var content) &&
                                content.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(content.GetString()))
                            {
                                hasNonEmptyContent = true;
                                bestTextChoice = choice;
                            }
                        }

                        if (hasToolCalls && !hasNonEmptyContent)
                            continue;

                        candidateChoices.Add(choice);
                    }

                    if (candidateChoices.Count > 0)
                    {
                        var selectedChoice = candidateChoices.Count == 1
                            ? candidateChoices[0]
                            : bestTextChoice ?? candidateChoices[^1];

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
                                        else if (cp.Name == "message" &&
                                                 cp.Value.ValueKind == JsonValueKind.Object)
                                        {
                                            writer.WriteStartObject("message");
                                            foreach (var mp in cp.Value.EnumerateObject())
                                            {
                                                if (!string.Equals(mp.Name, "tool_calls",
                                                        StringComparison.Ordinal))
                                                    mp.WriteTo(writer);
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
            catch { /* ignored */ }
        }

        buffer.Position = 0;
        context.Response.Body = originalBody;
        context.Response.ContentLength = buffer.Length;
        await buffer.CopyToAsync(originalBody);
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
    private sealed class ApprovalDecision
    {
        public bool Approved { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_webApp != null)
            await _webApp.DisposeAsync();
    }
}

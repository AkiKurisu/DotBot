using System.Collections.Concurrent;
using System.Text.Json;
using DotBot.Security;

namespace DotBot.Acp;

/// <summary>
/// ACP-based approval service that sends permission requests to the editor client
/// via the JSON-RPC requestPermission method.
/// </summary>
public sealed class AcpApprovalService(AcpTransport transport) : IApprovalService
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<RequestPermissionResult>> _pendingRequests = new();
    private int _nextRequestId;
    private string _sessionId = "";

    private readonly HashSet<string> _sessionApprovedOps = [];
    private readonly Lock _sessionLock = new();

    public void SetSessionId(string sessionId)
    {
        _sessionId = sessionId;
    }

    public async Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
    {
        var opKey = $"file:{operation}:{path}";
        lock (_sessionLock)
        {
            if (_sessionApprovedOps.Contains(opKey) || _sessionApprovedOps.Contains($"file:{operation}:*"))
                return true;
        }

        var toolCall = new AcpToolCallInfo
        {
            ToolCallId = Guid.NewGuid().ToString("N")[..12],
            Title = $"File {operation}: {path}",
            Kind = operation.ToLowerInvariant() switch
            {
                "read" or "list" => AcpToolKind.Read,
                "write" or "edit" => AcpToolKind.Edit,
                "delete" => AcpToolKind.Delete,
                _ => AcpToolKind.Other
            },
            Status = AcpToolStatus.Pending
        };

        var result = await SendPermissionRequestAsync(toolCall);
        if (result == null) return false;

        switch (result.Kind)
        {
            case AcpPermissionKind.AllowAlways:
                lock (_sessionLock) { _sessionApprovedOps.Add($"file:{operation}:*"); }
                return true;
            case AcpPermissionKind.AllowOnce:
                return true;
            default:
                return false;
        }
    }

    public async Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
    {
        lock (_sessionLock)
        {
            if (_sessionApprovedOps.Contains("shell:*"))
                return true;
        }

        var toolCall = new AcpToolCallInfo
        {
            ToolCallId = Guid.NewGuid().ToString("N")[..12],
            Title = $"Shell: {(command.Length > 80 ? command[..80] + "..." : command)}",
            Kind = AcpToolKind.Execute,
            Status = AcpToolStatus.Pending
        };

        var result = await SendPermissionRequestAsync(toolCall);
        if (result == null) return false;

        switch (result.Kind)
        {
            case AcpPermissionKind.AllowAlways:
                lock (_sessionLock) { _sessionApprovedOps.Add("shell:*"); }
                return true;
            case AcpPermissionKind.AllowOnce:
                return true;
            default:
                return false;
        }
    }

    private async Task<RequestPermissionResult?> SendPermissionRequestAsync(AcpToolCallInfo toolCall)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<RequestPermissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        try
        {
            var requestParams = new RequestPermissionParams
            {
                SessionId = _sessionId,
                ToolCall = toolCall,
                Options =
                [
                    new PermissionOption { Kind = AcpPermissionKind.AllowOnce, Label = "Allow once" },
                    new PermissionOption { Kind = AcpPermissionKind.AllowAlways, Label = "Allow always" },
                    new PermissionOption { Kind = AcpPermissionKind.RejectOnce, Label = "Reject" }
                ]
            };

            transport.SendRequest(requestId, AcpMethods.RequestPermission, requestParams);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await using var reg = cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// Called by the ACP handler when a response to a permission request is received.
    /// </summary>
    public void HandlePermissionResponse(int requestId, JsonElement resultElement)
    {
        if (!_pendingRequests.TryGetValue(requestId, out var tcs)) return;

        try
        {
            var result = resultElement.Deserialize<RequestPermissionResult>();
            if (result != null)
                tcs.TrySetResult(result);
            else
                tcs.TrySetCanceled();
        }
        catch
        {
            tcs.TrySetCanceled();
        }
    }
}

using System.Text.Json;

namespace DotBot.Acp;

/// <summary>
/// Proxy for making Agent→Client JSON-RPC method calls.
/// Provides typed wrappers for fs and terminal operations.
/// </summary>
public sealed class AcpClientProxy
{
    private readonly AcpTransport _transport;
    private readonly ClientCapabilities? _capabilities;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AcpClientProxy(AcpTransport transport, ClientCapabilities? capabilities)
    {
        _transport = transport;
        _capabilities = capabilities;
    }

    public bool SupportsFileRead => _capabilities?.Fs?.ReadTextFile == true;
    public bool SupportsFileWrite => _capabilities?.Fs?.WriteTextFile == true;
    public bool SupportsTerminal => _capabilities?.Terminal?.Create == true;

    // ───── File system operations ─────

    /// <summary>
    /// Reads a text file via the client. Returns file content including unsaved editor changes.
    /// </summary>
    public async Task<string?> ReadTextFileAsync(string path, int? offset = null, int? limit = null, CancellationToken ct = default)
    {
        if (!SupportsFileRead) return null;

        var result = await _transport.SendClientRequestAsync(AcpMethods.FsReadTextFile,
            new FsReadTextFileParams { Path = path, Offset = offset, Limit = limit }, ct);

        var typed = result.Deserialize<FsReadTextFileResult>(JsonOptions);
        return typed?.Content;
    }

    /// <summary>
    /// Writes a text file via the client. The editor may show a diff preview.
    /// </summary>
    public async Task<bool> WriteTextFileAsync(string path, string content, CancellationToken ct = default)
    {
        if (!SupportsFileWrite) return false;

        var result = await _transport.SendClientRequestAsync(AcpMethods.FsWriteTextFile,
            new FsWriteTextFileParams { Path = path, Content = content }, ct);

        var typed = result.Deserialize<FsWriteTextFileResult>(JsonOptions);
        return typed?.Success ?? false;
    }

    // ───── Terminal operations ─────

    /// <summary>
    /// Creates a terminal in the client and executes the given command.
    /// </summary>
    public async Task<string?> CreateTerminalAsync(string command, string? cwd = null, Dictionary<string, string>? env = null, CancellationToken ct = default)
    {
        if (!SupportsTerminal) return null;

        var result = await _transport.SendClientRequestAsync(AcpMethods.TerminalCreate,
            new TerminalCreateParams { Command = command, Cwd = cwd, Env = env }, ct);

        var typed = result.Deserialize<TerminalCreateResult>(JsonOptions);
        return typed?.TerminalId;
    }

    /// <summary>
    /// Gets the output and optional exit code from a terminal.
    /// </summary>
    public async Task<(string output, int? exitCode)> GetTerminalOutputAsync(string terminalId, CancellationToken ct = default)
    {
        var result = await _transport.SendClientRequestAsync(AcpMethods.TerminalGetOutput,
            new TerminalGetOutputParams { TerminalId = terminalId }, ct);

        var typed = result.Deserialize<TerminalGetOutputResult>(JsonOptions);
        return (typed?.Output ?? "", typed?.ExitCode);
    }

    /// <summary>
    /// Waits for a terminal command to exit.
    /// </summary>
    public async Task<(string output, int? exitCode)> WaitForTerminalExitAsync(string terminalId, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var result = await _transport.SendClientRequestAsync(AcpMethods.TerminalWaitForExit,
            new TerminalWaitForExitParams { TerminalId = terminalId, Timeout = timeoutSeconds }, ct);

        var typed = result.Deserialize<TerminalGetOutputResult>(JsonOptions);
        return (typed?.Output ?? "", typed?.ExitCode);
    }

    /// <summary>
    /// Kills a terminal command.
    /// </summary>
    public async Task KillTerminalAsync(string terminalId, CancellationToken ct = default)
    {
        await _transport.SendClientRequestAsync(AcpMethods.TerminalKill,
            new TerminalKillParams { TerminalId = terminalId }, ct);
    }

    /// <summary>
    /// Releases a terminal.
    /// </summary>
    public async Task ReleaseTerminalAsync(string terminalId, CancellationToken ct = default)
    {
        await _transport.SendClientRequestAsync(AcpMethods.TerminalRelease,
            new TerminalReleaseParams { TerminalId = terminalId }, ct);
    }
}

using System.Collections.Concurrent;

namespace DotBot.WeCom;

/// <summary>
/// WeCom bot registry - manages bot entries, crypto instances, and message handlers.
/// </summary>
public class WeComBotRegistry
{
    private readonly ConcurrentDictionary<string, WeComBotEntry> _entries = new();
    
    private readonly ConcurrentDictionary<string, WeComHandlers> _handlers = new();
    
    private readonly ConcurrentDictionary<string, WeComBizMsgCrypt> _crypts = new();

    /// <summary>
    /// Register a bot with its crypto credentials.
    /// Call <see cref="SetHandlers"/> separately to attach message handlers.
    /// </summary>
    public void Register(string path, string token, string encodingAesKey)
    {
        path = NormalizePath(path);

        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("Token must not be empty", nameof(token));

        if (string.IsNullOrEmpty(encodingAesKey))
            throw new ArgumentException("EncodingAESKey must not be empty", nameof(encodingAesKey));

        _entries[path] = new WeComBotEntry
        {
            Path = path,
            Token = token,
            EncodingAesKey = encodingAesKey
        };

        _crypts[path] = new WeComBizMsgCrypt(token, encodingAesKey);
    }

    /// <summary>
    /// Attach message handlers to a registered bot path.
    /// </summary>
    public void SetHandlers(string path,
        TextMessageHandler? textHandler = null,
        CommonMessageHandler? commonHandler = null,
        EventMessageHandler? eventHandler = null)
    {
        path = NormalizePath(path);

        var handlers = _handlers.GetOrAdd(path, _ => new WeComHandlers());
        handlers.TextHandler = textHandler;
        handlers.CommonHandler = commonHandler;
        handlers.EventHandler = eventHandler;
    }

    public WeComBotEntry? GetEntry(string path) => _entries.GetValueOrDefault(path);

    public WeComHandlers? GetHandlers(string path) => _handlers.GetValueOrDefault(path);

    public WeComBizMsgCrypt? GetCrypt(string path) => _crypts.GetValueOrDefault(path);

    public bool Exists(string path) => _entries.ContainsKey(path);

    public IEnumerable<string> GetAllPaths() => _entries.Keys;

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path must not be empty", nameof(path));
        return path.StartsWith('/') ? path : "/" + path;
    }
}

/// <summary>
/// A registered bot entry (crypto credentials).
/// </summary>
public class WeComBotEntry
{
    public string Path { get; set; } = string.Empty;
    
    public string Token { get; set; } = string.Empty;
    
    public string EncodingAesKey { get; set; } = string.Empty;
}

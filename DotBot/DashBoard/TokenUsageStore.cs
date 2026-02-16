using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotBot.DashBoard;

public enum TokenUsageSource
{
    QQPrivate,
    QQGroup,
    WeCom
}

public sealed class TokenUsageRecord
{
    public TokenUsageSource Source { get; init; }

    public string UserId { get; init; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public long GroupId { get; init; }

    public string GroupName { get; set; } = string.Empty;

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class UserTokenUsage
{
    public string UserId { get; init; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    private long _totalInputTokens;
    private long _totalOutputTokens;
    private int _requestCount;

    public long TotalInputTokens => Interlocked.Read(ref _totalInputTokens);

    public long TotalOutputTokens => Interlocked.Read(ref _totalOutputTokens);

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public int RequestCount => Interlocked.CompareExchange(ref _requestCount, 0, 0);

    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.UtcNow;

    public void Add(long inputTokens, long outputTokens)
    {
        Interlocked.Add(ref _totalInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
        Interlocked.Increment(ref _requestCount);
        LastActiveAt = DateTimeOffset.UtcNow;
    }

    public void Load(long inputTokens, long outputTokens, int requestCount, DateTimeOffset lastActiveAt)
    {
        Interlocked.Add(ref _totalInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
        Interlocked.Add(ref _requestCount, requestCount);
        if (lastActiveAt > LastActiveAt)
            LastActiveAt = lastActiveAt;
    }
}

public sealed class GroupTokenUsage
{
    public long GroupId { get; init; }

    public string GroupName { get; set; } = string.Empty;

    public ConcurrentDictionary<string, UserTokenUsage> Users { get; } = new();

    public long TotalInputTokens => Users.Values.Sum(u => u.TotalInputTokens);

    public long TotalOutputTokens => Users.Values.Sum(u => u.TotalOutputTokens);

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public int TotalRequestCount => Users.Values.Sum(u => u.RequestCount);

    public DateTimeOffset LastActiveAt => Users.Values.Count > 0
        ? Users.Values.Max(u => u.LastActiveAt)
        : DateTimeOffset.MinValue;
}

public sealed class TokenUsageStore
{
    private readonly string? _storagePath;

    private readonly ConcurrentDictionary<string, UserTokenUsage> _qqPrivateUsers = new();

    private readonly ConcurrentDictionary<long, GroupTokenUsage> _qqGroups = new();

    private readonly ConcurrentDictionary<string, UserTokenUsage> _wecomUsers = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public TokenUsageStore(string? storagePath = null)
    {
        _storagePath = storagePath;
    }

    public void Record(TokenUsageRecord record)
    {
        switch (record.Source)
        {
            case TokenUsageSource.QQPrivate:
            {
                var user = _qqPrivateUsers.GetOrAdd(record.UserId, _ => new UserTokenUsage
                {
                    UserId = record.UserId
                });
                user.DisplayName = record.DisplayName;
                user.Add(record.InputTokens, record.OutputTokens);
                break;
            }
            case TokenUsageSource.QQGroup:
            {
                var group = _qqGroups.GetOrAdd(record.GroupId, _ => new GroupTokenUsage
                {
                    GroupId = record.GroupId
                });
                if (!string.IsNullOrEmpty(record.GroupName))
                    group.GroupName = record.GroupName;

                var user = group.Users.GetOrAdd(record.UserId, _ => new UserTokenUsage
                {
                    UserId = record.UserId
                });
                user.DisplayName = record.DisplayName;
                user.Add(record.InputTokens, record.OutputTokens);
                break;
            }
            case TokenUsageSource.WeCom:
            {
                var user = _wecomUsers.GetOrAdd(record.UserId, _ => new UserTokenUsage
                {
                    UserId = record.UserId
                });
                user.DisplayName = record.DisplayName;
                user.Add(record.InputTokens, record.OutputTokens);
                break;
            }
        }

        if (_storagePath != null)
            PersistRecord(record);
    }

    public IReadOnlyList<UserTokenUsage> GetQQPrivateUsers()
    {
        return _qqPrivateUsers.Values
            .OrderByDescending(u => u.TotalTokens)
            .ToList();
    }

    public IReadOnlyList<GroupTokenUsage> GetQQGroups()
    {
        return _qqGroups.Values
            .OrderByDescending(g => g.TotalTokens)
            .ToList();
    }

    public IReadOnlyList<UserTokenUsage> GetWeComUsers()
    {
        return _wecomUsers.Values
            .OrderByDescending(u => u.TotalTokens)
            .ToList();
    }

    public TokenUsageSummary GetSummary()
    {
        long qqInput = 0, qqOutput = 0;
        int qqRequests = 0;

        foreach (var u in _qqPrivateUsers.Values)
        {
            qqInput += u.TotalInputTokens;
            qqOutput += u.TotalOutputTokens;
            qqRequests += u.RequestCount;
        }

        foreach (var g in _qqGroups.Values)
        {
            qqInput += g.TotalInputTokens;
            qqOutput += g.TotalOutputTokens;
            qqRequests += g.TotalRequestCount;
        }

        long wecomInput = 0, wecomOutput = 0;
        int wecomRequests = 0;

        foreach (var u in _wecomUsers.Values)
        {
            wecomInput += u.TotalInputTokens;
            wecomOutput += u.TotalOutputTokens;
            wecomRequests += u.RequestCount;
        }

        return new TokenUsageSummary
        {
            QQTotalInputTokens = qqInput,
            QQTotalOutputTokens = qqOutput,
            QQTotalRequests = qqRequests,
            QQPrivateUserCount = _qqPrivateUsers.Count,
            QQGroupCount = _qqGroups.Count,
            WeComTotalInputTokens = wecomInput,
            WeComTotalOutputTokens = wecomOutput,
            WeComTotalRequests = wecomRequests,
            WeComUserCount = _wecomUsers.Count
        };
    }

    public void LoadFromDisk()
    {
        if (_storagePath == null)
            return;

        var filePath = Path.Combine(_storagePath, "token_usage.jsonl");
        if (!File.Exists(filePath))
            return;

        try
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var record = JsonSerializer.Deserialize<TokenUsageRecord>(line, JsonOptions);
                    if (record == null) continue;

                    switch (record.Source)
                    {
                        case TokenUsageSource.QQPrivate:
                        {
                            var user = _qqPrivateUsers.GetOrAdd(record.UserId, _ => new UserTokenUsage
                            {
                                UserId = record.UserId
                            });
                            user.DisplayName = record.DisplayName;
                            user.Load(record.InputTokens, record.OutputTokens, 1, record.Timestamp);
                            break;
                        }
                        case TokenUsageSource.QQGroup:
                        {
                            var group = _qqGroups.GetOrAdd(record.GroupId, _ => new GroupTokenUsage
                            {
                                GroupId = record.GroupId
                            });
                            if (!string.IsNullOrEmpty(record.GroupName))
                                group.GroupName = record.GroupName;

                            var user = group.Users.GetOrAdd(record.UserId, _ => new UserTokenUsage
                            {
                                UserId = record.UserId
                            });
                            user.DisplayName = record.DisplayName;
                            user.Load(record.InputTokens, record.OutputTokens, 1, record.Timestamp);
                            break;
                        }
                        case TokenUsageSource.WeCom:
                        {
                            var user = _wecomUsers.GetOrAdd(record.UserId, _ => new UserTokenUsage
                            {
                                UserId = record.UserId
                            });
                            user.DisplayName = record.DisplayName;
                            user.Load(record.InputTokens, record.OutputTokens, 1, record.Timestamp);
                            break;
                        }
                    }
                }
                catch
                {
                    // Skip corrupted lines
                }
            }
        }
        catch
        {
            // Skip corrupted file
        }
    }

    private void PersistRecord(TokenUsageRecord record)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(_storagePath!);
                var filePath = Path.Combine(_storagePath!, "token_usage.jsonl");
                var json = JsonSerializer.Serialize(record, JsonOptions);
                lock (_fileLock)
                {
                    File.AppendAllText(filePath, json + "\n");
                }
            }
            catch
            {
                // Silently ignore persistence errors
            }
        });
    }

    private readonly object _fileLock = new();
}

public sealed class TokenUsageSummary
{
    public long QQTotalInputTokens { get; init; }

    public long QQTotalOutputTokens { get; init; }

    public int QQTotalRequests { get; init; }

    public int QQPrivateUserCount { get; init; }

    public int QQGroupCount { get; init; }

    public long WeComTotalInputTokens { get; init; }

    public long WeComTotalOutputTokens { get; init; }

    public int WeComTotalRequests { get; init; }

    public int WeComUserCount { get; init; }
}

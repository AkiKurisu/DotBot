using System.Text;

namespace DotBot.Memory;

/// <summary>
/// Memory system supporting daily notes (memory/YYYY-MM-DD.md) and long-term memory (MEMORY.md).
/// </summary>
public sealed class MemoryStore
{
    private readonly string _memoryDir;
    
    private readonly string _longTermFile;

    public MemoryStore(string workspaceRoot)
    {
        _memoryDir = Path.Combine(workspaceRoot, "memory");
        Directory.CreateDirectory(_memoryDir);
        _longTermFile = Path.Combine(_memoryDir, "MEMORY.md");
    }

    /// <summary>
    /// Get today's memory file path (YYYY-MM-DD.md).
    /// </summary>
    public string GetTodayFile()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(_memoryDir, $"{today}.md");
    }

    /// <summary>
    /// Read today's memory notes.
    /// </summary>
    public string ReadToday()
    {
        var todayFile = GetTodayFile();
        return File.Exists(todayFile) ? File.ReadAllText(todayFile, Encoding.UTF8) : string.Empty;
    }

    /// <summary>
    /// Append content to today's memory notes.
    /// </summary>
    public void AppendToday(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        var todayFile = GetTodayFile();
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        string finalContent;
        if (File.Exists(todayFile))
        {
            var existing = File.ReadAllText(todayFile, Encoding.UTF8);
            finalContent = existing + "\n" + content.Trim();
        }
        else
        {
            var header = $"# {today}\n\n";
            finalContent = header + content.Trim();
        }

        File.WriteAllText(todayFile, finalContent, Encoding.UTF8);
    }

    /// <summary>
    /// Read long-term memory (MEMORY.md).
    /// </summary>
    public string ReadLongTerm()
    {
        return File.Exists(_longTermFile) ? File.ReadAllText(_longTermFile, Encoding.UTF8) : string.Empty;
    }

    /// <summary>
    /// Write to long-term memory (MEMORY.md).
    /// </summary>
    public void WriteLongTerm(string content)
    {
        File.WriteAllText(_longTermFile, content, Encoding.UTF8);
    }

    /// <summary>
    /// Get memories from the last N days.
    /// </summary>
    public string GetRecentMemories(int days = 7)
    {
        var memories = new List<string>();
        var today = DateTime.Now.Date;

        for (var i = 0; i < days; i++)
        {
            var date = today.AddDays(-i);
            var dateStr = date.ToString("yyyy-MM-dd");
            var filePath = Path.Combine(_memoryDir, $"{dateStr}.md");

            if (File.Exists(filePath))
            {
                var content = File.ReadAllText(filePath, Encoding.UTF8);
                memories.Add(content);
            }
        }

        return memories.Count > 0 ? string.Join("\n\n---\n\n", memories) : string.Empty;
    }

    /// <summary>
    /// List all daily memory files sorted by date (newest first).
    /// </summary>
    public List<string> ListMemoryFiles()
    {
        if (!Directory.Exists(_memoryDir))
            return [];

        return Directory.GetFiles(_memoryDir, "????-??-??.md")
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>
    /// Get combined memory context for agent (long-term + today's notes).
    /// </summary>
    public string GetMemoryContext()
    {
        var parts = new List<string>();

        var longTerm = ReadLongTerm();
        if (!string.IsNullOrWhiteSpace(longTerm))
            parts.Add("## Long-term Memory\n" + longTerm);

        var today = ReadToday();
        if (!string.IsNullOrWhiteSpace(today))
            parts.Add("## Today's Notes\n" + today);

        return parts.Count > 0 ? string.Join("\n\n", parts) : string.Empty;
    }

    /// <summary>
    /// Add memory item (stores to today's notes).
    /// </summary>
    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        AppendToday($"- [{timestamp}] {text.Trim()}");
    }

    /// <summary>
    /// Get all memories (returns recent 7 days as list).
    /// </summary>
    public List<MemoryItem> GetAll()
    {
        var items = new List<MemoryItem>();
        var files = ListMemoryFiles().Take(7);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file, Encoding.UTF8);
            var date = Path.GetFileNameWithoutExtension(file);
            items.Add(new MemoryItem
            {
                Text = $"[{date}]\n{content}",
                CreatedAt = DateTime.Parse(date)
            });
        }

        return items;
    }

    public sealed class MemoryItem
    {
        public string Text { get; set; } = string.Empty;
        
        public DateTimeOffset CreatedAt { get; set; }
    }
}

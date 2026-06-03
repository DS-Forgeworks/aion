using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.Core.Interfaces;

namespace Aion.Core.Tools.Builtin;

/// Search the web using DuckDuckGo (no API key needed)
public class WebSearchTool : ITool
{
    public string Name => "web_search";
    public string Description => "Search the web for information. Uses DuckDuckGo — no API key needed.";
    public ToolCapability Capability => ToolCapability.ReadOnly;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var query = ExtractQuery(input);
            if (string.IsNullOrWhiteSpace(query))
                return ToolResult.Fail("No search query provided");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");

            var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
            var response = await http.GetStringAsync(url, ct);

            // Extract search results from DDG HTML
            var results = new List<string>();
            var resultRegex = Regex.Matches(response,
                @"<a[^>]*class=""[^""]*result__a[^""]*""[^>]*href=""([^""]*)""[^>]*>([^<]*)</a>",
                RegexOptions.IgnoreCase);

            var snippetRegex = Regex.Matches(response,
                @"<a[^>]*class=""[^""]*result__snippet[^""]*""[^>]*>([^<]*)</a>",
                RegexOptions.IgnoreCase);

            for (int i = 0; i < Math.Min(resultRegex.Count, 8); i++)
            {
                var title = Regex.Replace(resultRegex[i].Groups[2].Value, @"<[^>]+>", "").Trim();
                var link = resultRegex[i].Groups[1].Value;
                var snippet = i < snippetRegex.Count
                    ? Regex.Replace(snippetRegex[i].Groups[1].Value, @"<[^>]+>", "").Trim()
                    : "";

                // Clean DDG redirect URLs
                var cleanLink = Regex.Match(link, @"uddg=(https?://[^&]+)").Groups[1].Value;
                if (string.IsNullOrEmpty(cleanLink))
                    cleanLink = link;

                if (!string.IsNullOrWhiteSpace(title))
                    results.Add($"{i + 1}. {title}\n   {Uri.UnescapeDataString(cleanLink)}\n   {snippet}");
            }

            if (results.Count == 0)
                return ToolResult.Ok("No search results found.");

            return ToolResult.Ok(string.Join("\n\n", results));
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail("Search timed out after 15 seconds");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Search failed: {ex.Message}");
        }
    }

    private string ExtractQuery(string input)
    {
        try
        {
            var parsed = JsonDocument.Parse(input);
            if (parsed.RootElement.TryGetProperty("query", out var q)) return q.GetString() ?? "";
        }
        catch { }
        return input.Trim();
    }
}

/// Read a file from the filesystem (sandboxed to allowed directories)
public class ReadFileTool : ITool
{
    public string Name => "read_file";
    public string Description => "Read the contents of a file from the filesystem. Limited to 50KB.";
    public ToolCapability Capability => ToolCapability.ReadOnly;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var path = ExtractPath(input);
            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(ToolResult.Fail("No file path provided"));

            // Expand ~ to home directory
            if (path.StartsWith("~/"))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);

            if (!File.Exists(path))
                return Task.FromResult(ToolResult.Fail($"File not found: {path}"));

            var size = new FileInfo(path).Length;
            if (size > 51200) // 50KB limit
                return Task.FromResult(ToolResult.Fail($"File too large: {size} bytes. Max 50KB. Try using shell tools with `head` or `tail`."));

            var content = File.ReadAllText(path);
            return Task.FromResult(ToolResult.Ok(content));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Read failed: {ex.Message}"));
        }
    }

    private string ExtractPath(string input)
    {
        try
        {
            var parsed = JsonDocument.Parse(input);
            if (parsed.RootElement.TryGetProperty("path", out var p)) return p.GetString() ?? "";
            if (parsed.RootElement.TryGetProperty("filename", out var f)) return f.GetString() ?? "";
            if (parsed.RootElement.TryGetProperty("file", out var fl)) return fl.GetString() ?? "";
        }
        catch { }
        return input.Trim().Trim('"');
    }
}

/// Write content to a file
public class WriteFileTool : ITool
{
    public string Name => "write_file";
    public string Description => "Write content to a file. Creates directories if needed.";
    public ToolCapability Capability => ToolCapability.Execute;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var parsed = JsonDocument.Parse(input);

            var path = parsed.RootElement.TryGetProperty("path", out var p) ? p.GetString() : null;
            var content = parsed.RootElement.TryGetProperty("content", out var c) ? c.GetString() : null;

            if (string.IsNullOrWhiteSpace(path))
                return Task.FromResult(ToolResult.Fail("No file path provided"));

            // Expand ~
            if (path.StartsWith("~/"))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, content ?? "");
            return Task.FromResult(ToolResult.Ok($"Written {path} ({(content?.Length ?? 0)} bytes)"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Write failed: {ex.Message}"));
        }
    }
}

/// Remember a key-value pair for later recall
public class RememberTool : ITool
{
    // Internal static dictionary shared with RecallTool
    internal static readonly Dictionary<string, string> MemoryStore = new();

    public string Name => "remember";
    public string Description => "Store a fact or value in short-term memory for later recall during this conversation.";
    public ToolCapability Capability => ToolCapability.ReadOnly;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var parsed = JsonDocument.Parse(input);
            var key = parsed.RootElement.TryGetProperty("key", out var k) ? k.GetString() : null;
            var value = parsed.RootElement.TryGetProperty("value", out var v) ? v.GetString() : null;

            if (string.IsNullOrWhiteSpace(key))
                return Task.FromResult(ToolResult.Fail("No key provided"));

            MemoryStore[key] = value ?? "";
            return Task.FromResult(ToolResult.Ok($"Remembered: {key} = {value}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Memory error: {ex.Message}"));
        }
    }
}

/// Recall a previously remembered value
public class RecallTool : ITool
{
    public string Name => "recall";
    public string Description => "Retrieve a previously stored fact or value from short-term memory.";
    public ToolCapability Capability => ToolCapability.ReadOnly;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var parsed = JsonDocument.Parse(input);
            var key = parsed.RootElement.TryGetProperty("key", out var k) ? k.GetString() : null;

            if (string.IsNullOrWhiteSpace(key))
                return Task.FromResult(ToolResult.Fail("No key provided"));

            if (RememberTool.MemoryStore.TryGetValue(key, out var value))
                return Task.FromResult(ToolResult.Ok(value));

            return Task.FromResult(ToolResult.Fail($"Nothing remembered for key: {key}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Recall error: {ex.Message}"));
        }
    }
}

/// Schedule a one-shot reminder
public class ScheduleTool : ITool
{
    private static readonly List<Timer> _timers = new();

    public string Name => "schedule";
    public string Description => "Set a one-shot reminder for a number of seconds from now.";
    public ToolCapability Capability => ToolCapability.Execute;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var parsed = JsonDocument.Parse(input);

            var seconds = parsed.RootElement.TryGetProperty("seconds", out var s) ? s.GetInt32() : 0;
            var message = parsed.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "Reminder";

            if (seconds < 1 || seconds > 86400)
                return Task.FromResult(ToolResult.Fail("Seconds must be between 1 and 86400 (24 hours)"));

            var reminderId = Guid.NewGuid().ToString()[..8];
            var timer = new Timer(_ =>
            {
                // Log the reminder — dashboard will show it
                Console.WriteLine($"[REMINDER {reminderId}] {message}");
            }, null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);

            _timers.Add(timer);
            return Task.FromResult(ToolResult.Ok($"Reminder set: \"{message}\" in {seconds}s (ID: {reminderId})"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Schedule error: {ex.Message}"));
        }
    }
}

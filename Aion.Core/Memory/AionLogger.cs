using System.Collections.Concurrent;
using Aion.Core.Interfaces;

namespace Aion.Core.Memory;

public class AionLogger : IAionLogger
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly int _maxEntries;
    private readonly object _lock = new();

    public AionLogger(int maxEntries = 10000)
    {
        _maxEntries = maxEntries;
    }

    public void Log(LogLevel level, string source, string message, string? agentId = null,
        string? runId = null, object? data = null)
    {
        try { Console.Error.WriteLine($"[{level}] [{source}] {message}"); } catch { }
        var entry = new LogEntry(
            DateTime.UtcNow.ToString("O"),
            level, source, agentId, runId, message, data
        );

        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _maxEntries && _entries.TryDequeue(out _)) { }
        }
    }

    public void Debug(string source, string message, string? agentId = null, object? data = null)
        => Log(LogLevel.Debug, source, message, agentId, data: data);

    public void Info(string source, string message, string? agentId = null, object? data = null)
        => Log(LogLevel.Info, source, message, agentId, data: data);

    public void Warn(string source, string message, string? agentId = null, object? data = null)
        => Log(LogLevel.Warn, source, message, agentId, data: data);

    public void Error(string source, string message, string? agentId = null, object? data = null)
        => Log(LogLevel.Error, source, message, agentId, data: data);

    public void Crit(string source, string message, string? agentId = null, object? data = null)
        => Log(LogLevel.Crit, source, message, agentId, data: data);

    public Task<List<LogEntry>> QueryAsync(LogLevel? minLevel = null, string? agentId = null,
        DateTime? since = null, int limit = 50)
    {
        var query = _entries.AsEnumerable();

        if (minLevel.HasValue)
            query = query.Where(e => e.Level >= minLevel.Value);

        if (!string.IsNullOrEmpty(agentId))
            query = query.Where(e => e.AgentId == agentId);

        if (since.HasValue)
            query = query.Where(e => DateTime.Parse(e.Timestamp) >= since.Value);

        return Task.FromResult(query
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToList());
    }
}

namespace Aion.Core.Interfaces;

public enum LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3, Crit = 4 }

public record LogEntry(string Timestamp, LogLevel Level, string Source,
    string? AgentId, string? RunId, string Message, object? Data = null);

public interface IAionLogger
{
    void Log(LogLevel level, string source, string message, string? agentId = null,
        string? runId = null, object? data = null);
    void Debug(string source, string message, string? agentId = null, object? data = null);
    void Info(string source, string message, string? agentId = null, object? data = null);
    void Warn(string source, string message, string? agentId = null, object? data = null);
    void Error(string source, string message, string? agentId = null, object? data = null);
    void Crit(string source, string message, string? agentId = null, object? data = null);
    Task<List<LogEntry>> QueryAsync(LogLevel? minLevel = null, string? agentId = null,
        DateTime? since = null, int limit = 50);
}

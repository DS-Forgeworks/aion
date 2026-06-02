namespace Aion.Core.Interfaces;

public enum ToolCapability { ReadOnly = 1, Write = 2, Execute = 3, Root = 4 }

public record ToolResult(bool Success, string? Output, string? Error, double Confidence = 1.0)
{
    public static ToolResult Ok(string output) => new(true, output, null);
    public static ToolResult Fail(string error) => new(false, null, error);
}

public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolCapability Capability { get; }
    Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default);
}

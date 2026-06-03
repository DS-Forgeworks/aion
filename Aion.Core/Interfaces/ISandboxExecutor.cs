using Aion.Core.Interfaces;

namespace Aion.Core.Interfaces;

/// <summary>
/// Sandbox execution result.
/// </summary>
public record SandboxResult(bool Success, string? Output, string? Error, string? Language, long DurationMs);

/// <summary>
/// Executes arbitrary code in an isolated sandbox (Docker container).
/// Supports Python, JavaScript, Go, Ruby, Rust, C#, and any language
/// available in a Docker image.
/// </summary>
public interface ISandboxExecutor
{
    /// <summary>
    /// Execute code in a sandboxed container.
    /// </summary>
    /// <param name="code">The source code to run</param>
    /// <param name="language">Language key (python, node, go, ruby, rust, dotnet)</param>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <param name="memoryMb">Memory limit in megabytes</param>
    /// <param name="ct">Cancellation token</param>
    Task<SandboxResult> ExecuteAsync(string code, string language, int timeoutMs = 30000, int memoryMb = 256, CancellationToken ct = default);

    /// <summary>
    /// Check if Docker is available on this system.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Pull a Docker image for a language.
    /// </summary>
    Task<bool> EnsureImageAsync(string language, CancellationToken ct = default);
}

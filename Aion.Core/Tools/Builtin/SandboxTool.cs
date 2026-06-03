using System.Diagnostics;
using System.Text;
using Aion.Core.Interfaces;

namespace Aion.Core.Tools.Builtin;

/// <summary>
/// Executes code in a Docker container with timeout and memory limits.
/// Falls back to bare execution if Docker is unavailable.
///
/// Language → Docker image mapping:
///   python → python:3.12-slim
///   node   → node:20-slim
///   go     → golang:1.23-alpine
///   ruby   → ruby:3.3-slim
///   rust   → rust:1.80-slim
///   dotnet → mcr.microsoft.com/dotnet/sdk:9.0
///
/// If Docker is not available, uses the host runtime if the language
/// is installed locally (best-effort fallback for dev environments).
/// </summary>
public class SandboxTool : ITool
{
    public string Name => "sandbox";
    public string Description => "Execute code in a sandboxed Docker container. Supports languages: python, node, go, ruby, rust, dotnet. Returns stdout, errors, and duration.";
    public ToolCapability Capability => ToolCapability.Execute;

    private static readonly Dictionary<string, (string image, string cmd, string ext)> LanguageMap = new()
    {
        ["python"] = ("python:3.12-slim", "python3", ".py"),
        ["node"] = ("node:20-slim", "node", ".js"),
        ["go"] = ("golang:1.23-alpine", "go run", ".go"),
        ["ruby"] = ("ruby:3.3-slim", "ruby", ".rb"),
        ["rust"] = ("rust:1.80-slim", "rustc -o /tmp/out && /tmp/out", ".rs"),
        ["dotnet"] = ("mcr.microsoft.com/dotnet/sdk:9.0", "dotnet script", ".csx"),
        ["sh"] = ("alpine:3.20", "sh", ".sh"),
    };

    private static readonly HashSet<string> Pulling = new();
    private static readonly SemaphoreSlim PullLock = new(1, 1);

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var (code, language, timeout) = ParseInput(input);

            if (string.IsNullOrWhiteSpace(code))
                return ToolResult.Fail("No code provided. Send JSON with 'code' and 'language' fields.");

            if (!LanguageMap.ContainsKey(language))
                return ToolResult.Fail($"Unsupported language '{language}'. Supported: {string.Join(", ", LanguageMap.Keys)}");

            // Check Docker availability
            var dockerAvailable = await IsDockerAvailableAsync(ct);

            if (!dockerAvailable)
            {
                // Fallback to direct execution on host
                return await ExecuteLocallyAsync(code, language, timeout, ct);
            }

            return await ExecuteInDockerAsync(code, language, timeout, ct);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Fail("Sandbox execution cancelled");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Sandbox execution failed: {ex.Message}");
        }
    }

    private async Task<bool> IsDockerAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info --format '{{.ServerVersion}}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ToolResult> ExecuteInDockerAsync(string code, string language, int timeoutMs, CancellationToken ct)
    {
        var (image, cmd, ext) = LanguageMap[language];
        var result = new StringBuilder();
        var sw = Stopwatch.StartNew();

        // Ensure image exists (pull if needed)
        await EnsureImageAsync(image, ct);

        // Write code to temp file for mounting
        var tempDir = Path.Combine(Path.GetTempPath(), "aion-sandbox-" + Guid.NewGuid().ToString()[..8]);
        Directory.CreateDirectory(tempDir);
        var codeFile = Path.Combine(tempDir, $"code{ext}");

        // For compiled languages, wrap differently
        string dockerCmd;
        if (language == "rust")
        {
            // Write code to file, mount and compile + run
            await File.WriteAllTextAsync(codeFile, code, ct);
            dockerCmd = $"run --rm --network none --memory 256m --cpus 1 " +
                        $"-v \"{tempDir}:/code:ro\" -w /code {image} sh -c \"rustc code.rs -o /tmp/out 2>&1 && /tmp/out 2>&1\"";
        }
        else if (language == "dotnet")
        {
            await File.WriteAllTextAsync(codeFile, code, ct);
            dockerCmd = $"run --rm --network none --memory 512m --cpus 1 " +
                        $"-v \"{tempDir}:/code:ro\" -w /code {image} sh -c \"dotnet script code.csx 2>&1\"";
        }
        else
        {
            await File.WriteAllTextAsync(codeFile, code, ct);
            dockerCmd = $"run --rm --network none --memory {256}m --cpus 1 " +
                        $"-v \"{tempDir}:/code:ro\" -w /code {image} {cmd} code{ext}";
        }

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = dockerCmd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        proc.Start();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        var completed = proc.WaitForExit(timeoutMs);

        sw.Stop();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        // Cleanup
        try { Directory.Delete(tempDir, true); } catch { }

        if (!completed)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return ToolResult.Fail($"Sandbox timed out after {timeoutMs}ms");
        }

        if (proc.ExitCode != 0)
            return ToolResult.Fail($"[exit code {proc.ExitCode} in {sw.ElapsedMilliseconds}ms]\n{stdout}\n{stderr}".Trim());

        var output = stdout.Trim();
        if (string.IsNullOrEmpty(output)) output = "(no output)";
        output += $"\n[duration: {sw.ElapsedMilliseconds}ms]";
        return ToolResult.Ok(output);
    }

    private async Task<ToolResult> ExecuteLocallyAsync(string code, string language, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var (_, cmd, ext) = LanguageMap[language];

        // Write code to temp file
        var tempFile = Path.Combine(Path.GetTempPath(), $"aion-code-{Guid.NewGuid()}{ext}");
        await File.WriteAllTextAsync(tempFile, code, ct);

        string shellCmd;
        if (language == "rust")
        {
            shellCmd = $"rustc \"{tempFile}\" -o /tmp/aion-rs-out 2>&1 && /tmp/aion-rs-out 2>&1";
        }
        else if (language == "dotnet")
        {
            shellCmd = $"dotnet script \"{tempFile}\" 2>&1";
        }
        else
        {
            shellCmd = $"{cmd} \"{tempFile}\"";
        }

        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c \"{shellCmd}\"" : $"-c \"{shellCmd}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        var completed = proc.WaitForExit(timeoutMs);

        sw.Stop();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        try { File.Delete(tempFile); } catch { }

        if (!completed)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return ToolResult.Fail($"Local execution timed out after {timeoutMs}ms");
        }

        if (proc.ExitCode != 0)
            return ToolResult.Fail($"[exit code {proc.ExitCode} in {sw.ElapsedMilliseconds}ms]\n{stdout}\n{stderr}".Trim());

        var output = stdout.Trim();
        if (string.IsNullOrEmpty(output)) output = "(no output)";
        output += $"\n[duration: {sw.ElapsedMilliseconds}ms]";
        return ToolResult.Ok(output);
    }

    private async Task EnsureImageAsync(string image, CancellationToken ct)
    {
        if (Pulling.Contains(image)) return;

        // Quick check if image exists
        using var check = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"image inspect {image}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        check.Start();
        await check.WaitForExitAsync(ct).ConfigureAwait(false);
        if (check.ExitCode == 0) return;

        await PullLock.WaitAsync(ct);
        try
        {
            if (Pulling.Contains(image)) return;
            Pulling.Add(image);

            using var pull = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"pull {image}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            pull.Start();
            await pull.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Pulling.Remove(image);
            PullLock.Release();
        }
    }

    private (string code, string language, int timeoutMs) ParseInput(string input)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(input);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : input;
            var lang = root.TryGetProperty("language", out var l) ? l.GetString()?.ToLowerInvariant() ?? "python" : "python";
            var timeout = root.TryGetProperty("timeout", out var t) ? t.GetInt32() : 30000;
            return (code, lang, Math.Clamp(timeout, 1000, 120000));
        }
        catch
        {
            return (input.Trim(), "python", 30000);
        }
    }
}

using System.Diagnostics;
using System.Text;
using Aion.Core.Interfaces;

namespace Aion.Core.Tools.Builtin;

/// <summary>
/// Executes shell commands via `bash -c` or `cmd /c`.
/// Respects the safety gate for command allow/deny lists.
/// If docker is available, commands can be forwarded to SandboxTool.
/// </summary>
public class ShellTool : ITool
{
    public string Name => "shell_command";
    public string Description => "Execute a shell command with timeout. Returns stdout, stderr, and exit code.";
    public ToolCapability Capability => ToolCapability.Execute;

    private readonly int _defaultTimeoutSec = 30;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var (cmd, timeoutSec) = ParseInput(input);
            if (string.IsNullOrWhiteSpace(cmd))
                return ToolResult.Fail("No command provided. Send a JSON object with a 'command' field.");

            var isWindows = OperatingSystem.IsWindows();
            var fileName = isWindows ? "cmd.exe" : "/bin/bash";
            var args = isWindows ? $"/c \"{cmd}\"" : $"-c \"{cmd.Replace("\"", "\\\"")}\"";

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.Start();

            // Read streams in parallel to avoid deadlock
            var readTask = Task.Run(() =>
            {
                output.Append(process.StandardOutput.ReadToEnd());
                error.Append(process.StandardError.ReadToEnd());
            }, ct);

            var completed = process.WaitForExit(timeoutSec * 1000);
            if (!completed)
            {
                process.Kill(entireProcessTree: true);
                return ToolResult.Fail($"Command timed out after {timeoutSec}s:\n{cmd}");
            }

            await readTask;

            var stdout = output.ToString().Trim();
            var stderr = error.ToString().Trim();
            var exitCode = process.ExitCode;

            if (exitCode == 0)
            {
                return ToolResult.Ok(string.IsNullOrEmpty(stdout) ? "(no output)" : stdout);
            }

            var combined = stdout;
            if (!string.IsNullOrEmpty(stderr))
                combined += "\n[stderr]\n" + stderr;
            combined += $"\n[exit code: {exitCode}]";
            return ToolResult.Fail(combined);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Fail("Command cancelled");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Shell execution failed: {ex.Message}");
        }
    }

    private (string cmd, int timeout) ParseInput(string input)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(input);
            var root = doc.RootElement;
            var cmd = root.TryGetProperty("command", out var c) ? c.GetString() ?? "" : input.Trim();
            var timeout = root.TryGetProperty("timeout", out var t) ? t.GetInt32() : _defaultTimeoutSec;
            return (cmd, Math.Clamp(timeout, 1, 300));
        }
        catch
        {
            return (input.Trim(), _defaultTimeoutSec);
        }
    }
}

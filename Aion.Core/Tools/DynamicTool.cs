using System.Text.Json;
using Aion.Core.Interfaces;

namespace Aion.Core.Tools;

/// <summary>
/// A tool created at runtime by an agent.
/// Stores the agent-defined tool name, description, and code to execute.
/// When invoked, it sends the code to the sandbox executor.
/// </summary>
public class DynamicTool : ITool
{
    public string Name { get; }
    public string Description { get; }
    public ToolCapability Capability => ToolCapability.Execute;

    private readonly string _code;
    private readonly string _language;
    private readonly int _timeoutMs;
    private readonly ISandboxExecutor _sandbox;

    public DynamicTool(string name, string description, string code, string language, ISandboxExecutor sandbox, int timeoutMs = 30000)
    {
        Name = name;
        Description = description;
        _code = code;
        _language = language;
        _sandbox = sandbox;
        _timeoutMs = timeoutMs;
    }

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        // Merge the dynamic tool's hardcoded code with runtime input
        // The agent's code receives the input as a command-line argument or stdin
        var runtimeInput = string.IsNullOrWhiteSpace(input) ? "" : input;

        // For Python/Node: pass input as an environment variable or ARGV
        string wrappedCode;
        switch (_language)
        {
            case "python":
                wrappedCode = $@"import sys, json, os
# AION dynamic tool: {Name}
# User input from args or stdin
user_input = ' '.join(sys.argv[1:]) if len(sys.argv) > 1 else os.environ.get('AION_INPUT', sys.stdin.read().strip())

{_code}
";
                break;
            case "node":
                wrappedCode = $@"// AION dynamic tool: {Name}
const userInput = process.argv.slice(2).join(' ') || process.env.AION_INPUT || '';

{_code}
";
                break;
            default:
                wrappedCode = _code;
                break;
        }

        return await _sandbox.ExecuteAsync(wrappedCode, _language, _timeoutMs, 256, ct) switch
        {
            { Success: true } r => ToolResult.Ok(r.Output ?? "(no output)"),
            { Success: false } r => ToolResult.Fail(r.Error ?? "sandbox failed"),
            _ => ToolResult.Fail("unknown sandbox error")
        };
    }
}

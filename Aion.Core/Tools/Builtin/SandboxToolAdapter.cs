using Aion.Core.Interfaces;

namespace Aion.Core.Tools.Builtin;

/// <summary>
/// Adapts SandboxTool (an ITool) to the ISandboxExecutor interface.
/// Used to inject the sandbox into DynamicTool and the service container.
/// </summary>
public class SandboxToolAdapter : ISandboxExecutor
{
    private readonly SandboxTool _tool;

    public SandboxToolAdapter(SandboxTool tool)
    {
        _tool = tool;
    }

    public async Task<SandboxResult> ExecuteAsync(string code, string language, int timeoutMs = 30000, int memoryMb = 256, CancellationToken ct = default)
    {
        var input = $"{{\"code\":{System.Text.Json.JsonSerializer.Serialize(code)},\"language\":\"{language}\",\"timeout\":{timeoutMs}}}";
        var result = await _tool.ExecuteAsync(input, ct);
        return new SandboxResult(result.Success, result.Output, result.Error, language, 0);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var result = await _tool.ExecuteAsync("{\"code\":\"print('ok')\",\"language\":\"python\",\"timeout\":5000}", ct);
        return result.Success;
    }

    public Task<bool> EnsureImageAsync(string language, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }
}

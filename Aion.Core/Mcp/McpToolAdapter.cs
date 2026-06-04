using System.Text.Json;
using Aion.Core.Interfaces;

namespace Aion.Core.Mcp;

/// <summary>
/// Adapts a remote MCP server tool into AION's ITool interface.
/// This lets AION's ToolRegistry treat MCP tools identical to in-process tools.
/// </summary>
public class McpToolAdapter : ITool
{
    private readonly McpManager _mcpManager;
    private readonly string _serverId;
    private readonly McpToolDefinition _toolDef;

    public McpToolAdapter(McpManager mcpManager, string serverId, McpToolDefinition toolDef)
    {
        _mcpManager = mcpManager;
        _serverId = serverId;
        _toolDef = toolDef;
    }

    public string Name => _toolDef.Name;
    public string Description => _toolDef.Description;
    public ToolCapability Capability => ToolCapability.Write; // conservative — MCP tools may modify state

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            // Parse the input as JSON if possible, otherwise wrap it as text argument
            JsonElement arguments;
            if (input.TrimStart().StartsWith("{"))
            {
                arguments = JsonSerializer.Deserialize<JsonElement>(input);
            }
            else
            {
                // Wrap plain string input into the first schema parameter
                arguments = JsonSerializer.Deserialize<JsonElement>(
                    $"{{\"input\": {JsonSerializer.Serialize(input)}}}");
            }

            var result = await _mcpManager.CallToolAsync(_serverId, _toolDef.Name, arguments, ct);
            
            if (result.Success)
                return ToolResult.Ok(result.Content);
            else
                return ToolResult.Fail(result.Error);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"MCP tool error: {ex.Message}");
        }
    }
}

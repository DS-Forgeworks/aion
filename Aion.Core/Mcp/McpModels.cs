using System.Text.Json;

namespace Aion.Core.Mcp;

/// <summary>
/// Information about a connected MCP server.
/// </summary>
public class McpServerInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int ToolCount { get; set; }
    public List<McpToolDefinition> Tools { get; set; } = new();
    public bool IsConnected { get; set; }
    public int Pid { get; set; }
}

/// <summary>
/// A tool registered by an MCP server.
/// </summary>
public class McpToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string InputSchema { get; set; } = "{}";
}

/// <summary>
/// Result from calling an MCP tool.
/// </summary>
public class McpToolResult
{
    public bool Success { get; set; }
    public string Content { get; set; } = "";
    public string Error { get; set; } = "";
}

using Aion.Core.Mcp;
using Aion.Core.Tools;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Aion.Host.Controllers;

[ApiController]
[Route("api/mcp")]
public class McpController : ControllerBase
{
    private readonly McpManager _mcpManager;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<McpController> _logger;

    public McpController(McpManager mcpManager, ToolRegistry toolRegistry, ILogger<McpController> logger)
    {
        _mcpManager = mcpManager;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    /// <summary>
    /// List all connected MCP servers.
    /// </summary>
    [HttpGet("servers")]
    public IActionResult ListServers()
    {
        var servers = _mcpManager.GetAllServerInfo();
        return Ok(servers);
    }

    /// <summary>
    /// Start a new MCP server by command.
    /// </summary>
    [HttpPost("servers")]
    public async Task<IActionResult> StartServer([FromBody] McpStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
            request.Id = Guid.NewGuid().ToString("N")[..8];

        try
        {
        var info = await _mcpManager.StartServerAsync(
            request.Id, request.Name, request.Command, 
            request.Args ?? Array.Empty<string>(), 
            request.Env, HttpContext.RequestAborted);

        // Register all tools from this server into ToolRegistry
        foreach (var tool in info.Tools)
        {
            var adapter = new McpToolAdapter(_mcpManager, request.Id, tool);
            _toolRegistry.Register(adapter);
            _logger.LogInformation("Registered MCP tool '{Tool}' from server '{Server}'", tool.Name, request.Name);
        }

            return Ok(info);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MCP server '{Name}'", request.Name);
            return StatusCode(500, new { error = $"Failed to start MCP server '{request.Name}': {ex.Message}" });
        }
    }

    /// <summary>
    /// Stop and remove an MCP server.
    /// </summary>
    [HttpDelete("servers/{serverId}")]
    public async Task<IActionResult> StopServer(string serverId)
    {
        var server = _mcpManager.GetServer(serverId);
        if (server == null)
            return NotFound(new { error = $"Server '{serverId}' not found" });

        // Unregister all tools from ToolRegistry
        foreach (var tool in server.Tools)
        {
            _toolRegistry.Unregister(tool.Name);
            _logger.LogInformation("Unregistered MCP tool '{Tool}' from server '{Server}'", tool.Name, server.Name);
        }

        await _mcpManager.StopServerAsync(serverId);
        return Ok(new { status = "stopped", id = serverId });
    }

    /// <summary>
    /// List all tools across all MCP servers.
    /// </summary>
    [HttpGet("tools")]
    public IActionResult ListTools()
    {
        var allTools = _mcpManager.GetAllTools();
        var result = allTools.Select(t => new
        {
            serverId = t.ServerId,
            name = t.Tool.Name,
            description = t.Tool.Description,
            inputSchema = t.Tool.InputSchema
        });
        return Ok(result);
    }

    /// <summary>
    /// Call a tool on an MCP server.
    /// </summary>
    [HttpPost("tools/{serverId}/{toolName}")]
    public async Task<IActionResult> CallTool(string serverId, string toolName, [FromBody] JsonElement arguments)
    {
        try
        {
            var result = await _mcpManager.CallToolAsync(serverId, toolName, arguments, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}

public class McpStartRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string[]? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
}

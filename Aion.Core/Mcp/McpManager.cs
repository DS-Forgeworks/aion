using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Aion.Core.Mcp;

/// <summary>
/// Manages external MCP (Model Context Protocol) servers as stdio subprocesses.
/// Each server exposes tools that AION's ToolRegistry can call.
/// Protocol: JSON-RPC 2.0 over stdin/stdout
/// </summary>
public class McpManager : IDisposable
{
    private readonly ConcurrentDictionary<string, McpServerProcess> _servers = new();
    private readonly ILogger<McpManager> _logger;

    public McpManager(ILogger<McpManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Start an MCP server subprocess and register its tools.
    /// </summary>
    public async Task<McpServerInfo> StartServerAsync(
        string serverId,
        string name,
        string command,
        string[] args,
        Dictionary<string, string>? env = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (env != null)
            foreach (var kv in env)
                psi.EnvironmentVariables[kv.Key] = kv.Value;

        var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogError(ex, "Failed to start MCP server {Name}: {Command} not found", name, command);
            throw new InvalidOperationException($"MCP server '{name}' failed to start: {command} not found. Check that it's installed.");
        }

        _logger.LogInformation("Started MCP server {Name} ({Id}) PID: {Pid}", name, serverId, process.Id);

        var server = new McpServerProcess(serverId, name, process, _logger);
        _servers[serverId] = server;

        server.StartStderrReader();

        // Do initial handshake: send tools/list
        List<McpToolDefinition> tools;
        try
        {
            tools = await server.ListToolsAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("MCP server {Name} timed out during handshake (process may have crashed)", name);
            _servers.TryRemove(serverId, out _);
            server.Dispose();
            throw new InvalidOperationException($"MCP server '{name}' did not respond to handshake (process crashed or didn't start).");
        }
        _logger.LogInformation("MCP server {Name} registered {Count} tools", name, tools.Count);

        return new McpServerInfo
        {
            Id = serverId,
            Name = name,
            ToolCount = tools.Count,
            Tools = tools,
            IsConnected = true,
            Pid = process.Id
        };
    }

    /// <summary>
    /// Call a tool on a specific MCP server.
    /// </summary>
    public async Task<McpToolResult> CallToolAsync(
        string serverId,
        string toolName,
        JsonElement arguments,
        CancellationToken ct = default)
    {
        if (!_servers.TryGetValue(serverId, out var server))
            throw new InvalidOperationException($"Server '{serverId}' not found");

        // Check if process has exited
        if (server.HasExited)
        {
            _servers.TryRemove(serverId, out _);
            throw new InvalidOperationException($"Server '{serverId}' has exited (crashed)");
        }

        return await server.CallToolAsync(toolName, arguments, ct);
    }

    /// <summary>
    /// Stop and dispose an MCP server.
    /// </summary>
    public async Task StopServerAsync(string serverId)
    {
        if (_servers.TryRemove(serverId, out var server))
        {
            server.Dispose();
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Get server info by ID, or null if not found.
    /// </summary>
    public McpServerInfo? GetServer(string serverId)
    {
        if (_servers.TryGetValue(serverId, out var server))
        {
            return new McpServerInfo
            {
                Id = serverId,
                Name = server.Name,
                ToolCount = server.RegisteredTools.Count,
                Tools = server.RegisteredTools.ToList(),
                IsConnected = !server.HasExited,
                Pid = 0 // not stored after process exit
            };
        }
        return null;
    }

    /// <summary>
    /// Get info for all servers.
    /// </summary>
    public List<McpServerInfo> GetAllServerInfo()
    {
        var result = new List<McpServerInfo>();
        foreach (var (id, server) in _servers)
        {
            result.Add(new McpServerInfo
            {
                Id = id,
                Name = server.Name,
                ToolCount = server.RegisteredTools.Count,
                Tools = server.RegisteredTools.ToList(),
                IsConnected = !server.HasExited,
                Pid = 0
            });
        }
        return result;
    }

    /// <summary>
    /// Discover which server handles a given tool name.
    /// </summary>
    public (string ServerId, McpToolDefinition Tool)? FindTool(string toolName)
    {
        foreach (var (id, server) in _servers)
        {
            foreach (var tool in server.RegisteredTools)
            {
                if (tool.Name == toolName)
                    return (id, tool);
            }
        }
        return null;
    }

    /// <summary>
    /// List all tools across all connected MCP servers.
    /// </summary>
    public List<(string ServerId, McpToolDefinition Tool)> GetAllTools()
    {
        var result = new List<(string, McpToolDefinition)>();
        foreach (var (id, server) in _servers)
            foreach (var tool in server.RegisteredTools)
                result.Add((id, tool));
        return result;
    }

    public void Dispose()
    {
        foreach (var (_, server) in _servers)
            server.Dispose();
        _servers.Clear();
    }
}

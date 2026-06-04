using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Aion.Core.Mcp;

/// <summary>
/// Represents a single MCP stdio subprocess.
/// Handles JSON-RPC 2.0 messaging over stdin/stdout.
/// Thread-safe: uses SemaphoreSlim for stdin writes to prevent interleaved requests.
/// </summary>
public class McpServerProcess : IDisposable
{
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly string _serverId;
    private readonly string _name;
    private readonly ConcurrentBag<McpToolDefinition> _tools = new();
    private int _requestId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pendingRequests = new();
    
    /// <summary>
    /// Serializes stdin writes. Without this, concurrent requests interleave JSON on stdin.
    /// </summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _outputReaderTask;

    public string ServerId => _serverId;
    public string Name => _name;
    public IReadOnlyCollection<McpToolDefinition> RegisteredTools => _tools;
    public bool HasExited => _process.HasExited;

    public McpServerProcess(string serverId, string name, Process process, ILogger logger)
    {
        _serverId = serverId;
        _name = name;
        _process = process;
        _logger = logger;

        // Start reading stdout on background thread
        _outputReaderTask = Task.Run(() => ReadOutputLoop(_shutdownCts.Token));
    }

    public void StartStderrReader()
    {
        // Stderr reader runs on background thread
        _ = Task.Run(async () =>
        {
            try
            {
                var reader = _process.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    _logger.LogDebug("[MCP:{Name}] stderr: {Line}", _name, line);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[MCP:{Name}] stderr reader failed", _name);
            }
        });
    }

    /// <summary>
    /// Send tools/list to discover all available tools.
    /// </summary>
    public async Task<List<McpToolDefinition>> ListToolsAsync(CancellationToken ct = default)
    {
        var response = await SendRequestAsync("tools/list", new JsonObject(), ct);

        var result = response["result"]?.AsObject();
        if (result != null && result.TryGetPropertyValue("tools", out var toolsNode))
        {
            var tools = new List<McpToolDefinition>();
            if (toolsNode is JsonArray toolsArray)
            {
                foreach (var tool in toolsArray)
                {
                    if (tool is JsonObject toolObj)
                    {
                        var def = new McpToolDefinition
                        {
                            Name = toolObj["name"]?.GetValue<string>() ?? "unknown",
                            Description = toolObj.TryGetPropertyValue("description", out var desc)
                                ? desc?.GetValue<string>() ?? "" : "",
                            InputSchema = toolObj.TryGetPropertyValue("inputSchema", out var schema)
                                ? schema?.ToJsonString() ?? "{}" : "{}"
                        };
                        tools.Add(def);
                        _tools.Add(def);
                    }
                }
            }
            return tools;
        }

        return new List<McpToolDefinition>();
    }

    /// <summary>
    /// Call a tool by name with arguments.
    /// </summary>
    public async Task<McpToolResult> CallToolAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken ct = default)
    {
        var args = JsonSerializer.SerializeToNode(arguments) ?? new JsonObject();
        var response = await SendRequestAsync("tools/call", new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = args
        }, ct);

        var result = new McpToolResult { Success = false };

        var resultObj = response["result"]?.AsObject();
        if (resultObj != null)
        {
            result.Success = true;
            if (resultObj.TryGetPropertyValue("content", out var content) && content is JsonArray contentArray)
            {
                var texts = new List<string>();
                foreach (var item in contentArray)
                {
                    if (item is JsonObject itemObj)
                    {
                        if (itemObj.TryGetPropertyValue("text", out var textNode))
                            texts.Add(textNode?.GetValue<string>() ?? "");
                        else if (itemObj.TryGetPropertyValue("type", out var typeNode)
                                 && typeNode?.GetValue<string>() == "text"
                                 && itemObj.TryGetPropertyValue("text", out var t))
                            texts.Add(t?.GetValue<string>() ?? "");
                    }
                }
                result.Content = string.Join("\n", texts);
            }
        }
        else
        {
            var errorObj = response["error"]?.AsObject();
            if (errorObj != null)
            {
                result.Success = false;
                result.Error = errorObj.TryGetPropertyValue("message", out var msg)
                    ? msg?.GetValue<string>() ?? "Unknown error" : "Unknown error";
            }
            else
            {
                result.Error = "MCP call returned no result and no error";
            }
        }

        return result;
    }

    private async Task<JsonObject> SendRequestAsync(
        string method,
        JsonObject @params,
        CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params
        };

        var json = request.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        }) + "\n";

        // Lock to serialize writes to stdin — prevents interleaved JSON on the pipe
        await _writeLock.WaitAsync(ct);
        try
        {
            await _process.StandardInput.WriteAsync(json);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }

        _logger.LogDebug("[MCP:{Name}] Sent: {Method} (id={Id})", _name, method, id);

        // Wait for response
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        // For tools/list (handshake), use a shorter timeout
        if (method == "tools/list")
            cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var responseJson = await tcs.Task.WaitAsync(cts.Token);
            var response = JsonSerializer.Deserialize<JsonObject>(responseJson)
                ?? new JsonObject { ["error"] = JsonNode.Parse("{\"message\":\"Empty response\"}") };
            return response;
        }
        catch (TimeoutException)
        {
            return new JsonObject
            {
                ["error"] = JsonNode.Parse("{\"message\":\"Request timed out\"}")
            };
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    private async Task ReadOutputLoop(CancellationToken ct)
    {
        try
        {
            var buffer = new char[65536];
            var sb = new StringBuilder();
            
            while (!ct.IsCancellationRequested && !_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync();
                if (line == null) break;

                // Try to parse as JSON response (id present = response, no id = notification)
                if (line.TrimStart().StartsWith("{"))
                {
                    try
                    {
                        var json = JsonSerializer.Deserialize<JsonObject>(line);
                        if (json != null)
                        {
                            // Check for response (has "id")
                            if (json.TryGetPropertyValue("id", out var idNode) &&
                                idNode?.GetValueKind() == JsonValueKind.Number)
                            {
                                var id = idNode.GetValue<int>();
                                if (_pendingRequests.TryRemove(id, out var tcs))
                                {
                                    tcs.TrySetResult(line);
                                }
                                else
                                {
                                    _logger.LogWarning("[MCP:{Name}] Orphaned response for id={Id}: {Line}",
                                        _name, id, line.Truncate(200));
                                }
                            }
                            // Notification (no "id") — could be progress
                            else if (json.TryGetPropertyValue("method", out var methodNode) &&
                                     methodNode?.GetValue<string>() == "notifications/progress" &&
                                     json.TryGetPropertyValue("params", out var p))
                            {
                                _logger.LogInformation("[MCP:{Name}] Progress: {Progress}",
                                    _name, p?.ToJsonString());
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Non-JSON lines are ignored
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MCP:{Name}] Output reader failed", _name);
        }
    }

    public void Dispose()
    {
        _shutdownCts.Cancel();
        if (!_process.HasExited)
        {
            try { _process.Kill(); } catch { }
            _process.WaitForExit(3000);
        }
        _process.Dispose();
        _shutdownCts.Dispose();
        _writeLock.Dispose();
    }
}

// Extension for truncation logging
internal static class StringExtensions
{
    public static string Truncate(this string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}

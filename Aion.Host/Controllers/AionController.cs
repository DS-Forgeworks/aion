using Aion.Core;
using Aion.Core.Configuration;
using Aion.Core.Interfaces;
using Aion.Core.Memory;
using Aion.Core.Models;
using Aion.Core.Services;
using Aion.Core.Safety;
using Aion.Core.Tools;
using Microsoft.AspNetCore.Mvc;

namespace Aion.Host.Controllers;

[ApiController]
[Route("api")]
public class AionController : ControllerBase
{
    private readonly AgentLoop _agentLoop;
    private readonly ToolRegistry _toolRegistry;
    private readonly IMemoryStore _memory;
    private readonly IPlanStore _planStore;
    private readonly IAionLogger _logger;
    private readonly IRateLimiter _rateLimiter;
    private readonly ISafetyGate _safety;
    private readonly AppConfig _config;

    public AionController(
        AgentLoop agentLoop, ToolRegistry toolRegistry,
        IMemoryStore memory, IPlanStore planStore,
        IAionLogger logger, IRateLimiter rateLimiter,
        ISafetyGate safety, AppConfig config)
    {
        _agentLoop = agentLoop;
        _toolRegistry = toolRegistry;
        _memory = memory;
        _planStore = planStore;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _safety = safety;
        _config = config;
    }

    // GET /api/health
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            version = "1.0.0",
            uptime = Environment.TickCount64 / 1000,
            agents = 0,
            llm_status = _config.Llm.Provider,
            errors_1h = 0
        });
    }

    // GET /api/version
    [HttpGet("version")]
    public IActionResult Version() => Ok(new { version = "1.0.0", build_date = DateTime.UtcNow.ToString("O") });

    // GET /api/agents
    [HttpGet("agents")]
    public IActionResult ListAgents() => Ok(Array.Empty<object>());

    // GET /api/tools
    [HttpGet("tools")]
    public IActionResult ListTools() => Ok(_toolRegistry.GetDefinitions());

    // POST /api/agents/{id}/message
    [HttpPost("agents/{id}/message")]
    public async Task<IActionResult> SendMessage(string id, [FromBody] MessageRequest req)
    {
        var rateCheck = await _rateLimiter.CheckAsync("user", "tool_calls");
        if (!rateCheck.Allowed)
            return StatusCode(429, new { ok = false, error = rateCheck.Error, error_code = "RATE_LIMITED" });

        var request = new AgentRequest(id, "user", req.Text ?? "", req.Mode ?? "chat", req.Model);
        var result = await _agentLoop.RunAsync(request);

        if (!result.Success)
            return StatusCode(500, new { ok = false, error = result.Error, error_code = "AGENT_ERROR" });

        return Ok(new { ok = true, reply = result.Reply });
    }

    // POST /api/agents/{id}/task
    [HttpPost("agents/{id}/task")]
    public async Task<IActionResult> SendTask(string id, [FromBody] TaskRequest req)
    {
        var request = new AgentRequest(id, "user", req.Text ?? "", "task", req.Model);
        var result = await _agentLoop.RunAsync(request);

        if (!result.Success)
            return StatusCode(500, new { ok = false, error = result.Error, error_code = "AGENT_ERROR" });

        return Ok(new { ok = true, plan_id = Guid.NewGuid().ToString(), reply = result.Reply });
    }

    // GET /api/memory/search
    [HttpGet("memory/search")]
    public async Task<IActionResult> SearchMemory([FromQuery] string q, [FromQuery] int limit = 5)
    {
        var results = await _memory.SearchAsync(q, limit);
        return Ok(results);
    }

    // POST /api/memory/store
    [HttpPost("memory/store")]
    public async Task<IActionResult> StoreMemory([FromBody] StoreMemoryRequest req)
    {
        var entry = new MemoryEntry(
            Guid.NewGuid().ToString(), req.Content ?? "", req.Tags, "api",
            DateTime.UtcNow);
        await _memory.StoreAsync(entry);
        return Ok(new { ok = true, id = entry.Id });
    }

    // POST /api/run
    [HttpPost("run")]
    public async Task<IActionResult> RunTool([FromBody] RunToolRequest req)
    {
        var rateCheck = await _rateLimiter.CheckAsync("user", "tool_calls");
        if (!rateCheck.Allowed)
            return StatusCode(429, new { ok = false, error = rateCheck.Error, error_code = "RATE_LIMITED" });

        var tool = _toolRegistry.Resolve(req.Tool ?? "");
        if (tool == null)
            return NotFound(new { ok = false, error = $"Tool '{req.Tool}' not found", error_code = "TOOL_NOT_FOUND" });

        var safetyCheck = await _safety.EvaluateAsync(req.Tool ?? "", req.Input ?? "", tool.Capability, ToolCapability.ReadOnly);
        if (!safetyCheck.Allowed)
            return StatusCode(403, new { ok = false, error = safetyCheck.DenyReason, error_code = "SAFETY_BLOCKED" });

        var result = await tool.ExecuteAsync(req.Input ?? "");
        return Ok(new { ok = result.Success, result = result.Output, error = result.Error });
    }

    // GET /api/logs
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? level, [FromQuery] string? agent_id,
        [FromQuery] string? since, [FromQuery] int limit = 50)
    {
        Aion.Core.Interfaces.LogLevel? minLevel = level?.ToUpper() switch
        {
            "DEBUG" => Aion.Core.Interfaces.LogLevel.Debug,
            "INFO" => Aion.Core.Interfaces.LogLevel.Info,
            "WARN" => Aion.Core.Interfaces.LogLevel.Warn,
            "ERROR" => Aion.Core.Interfaces.LogLevel.Error,
            "CRIT" => Aion.Core.Interfaces.LogLevel.Crit,
            _ => null
        };

        DateTime? sinceDt = since != null ? DateTime.Parse(since) : null;
        var entries = await _logger.QueryAsync(minLevel, agent_id, sinceDt, limit);
        return Ok(entries);
    }

    // GET /api/models — list available Ollama models
    [HttpGet("models")]
    public async Task<IActionResult> ListModels()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.GetStringAsync(
                $"{_config.Llm.Endpoint ?? "http://127.0.0.1:11434"}/api/tags");
            var doc = System.Text.Json.JsonDocument.Parse(resp);
            var models = doc.RootElement.GetProperty("models").EnumerateArray()
                .Select(m => new {
                    name = m.GetProperty("name").GetString(),
                    size = m.GetProperty("size").GetInt64(),
                    modified = m.GetProperty("modified_at").GetString()
                })
                .OrderByDescending(m => m.size)
                .ToList();
            return Ok(models);
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message, models = Array.Empty<object>() });
        }
    }

    // GET /api/config
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var masked = new
        {
            _config.Version,
            Llm = new { _config.Llm.Provider, _config.Llm.Model, _config.Llm.Endpoint, ApiKey = MaskHelper.MaskKey(_config.Llm.ApiKey) },
            _config.Language,
            Safety = new { _config.Safety.SafeMode, _config.Safety.ShellEnabled },
            _config.Workspace
        };
        return Ok(masked);
    }

    // POST /api/setup — configure LLM and save
    [HttpPost("setup")]
    public async Task<IActionResult> RunSetup([FromBody] SetupRequest req)
    {
        try
        {
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aion");
            Directory.CreateDirectory(configDir);

            var configManager = new Aion.Core.Configuration.ConfigManager(
                Path.Combine(configDir, "aion-config.json"));

            var cfg = configManager.Load();

            if (req.Llm != null)
            {
                cfg.Llm.Provider = req.Llm.Provider ?? cfg.Llm.Provider;
                cfg.Llm.Model = req.Llm.Model ?? cfg.Llm.Model;
                cfg.Llm.Endpoint = req.Llm.Endpoint;
                cfg.Llm.ApiKey = req.Llm.ApiKey;
            }

            if (req.Safety != null)
            {
                cfg.Safety.SafeMode = req.Safety.SafeMode;
                cfg.Safety.ShellEnabled = req.Safety.ShellEnabled;
            }

            if (req.Mesh != null)
            {
                cfg.Mesh.Enabled = req.Mesh.Enabled;
                cfg.Mesh.Port = req.Mesh.Port;
            }

            configManager.Save(cfg);
            _logger.Info("Setup", "Configuration saved via setup wizard");
            return Ok(new { ok = true, message = "Configuration saved. Restart server to apply all changes." });
        }
        catch (Exception ex)
        {
            _logger.Error("Setup", $"Setup failed: {ex.Message}");
            return StatusCode(500, new { ok = false, error = ex.Message });
        }
    }
}

public record MessageRequest(string? Text, string? Mode, string? Model);
public record TaskRequest(string? Text, string? Priority, string? Model = null);
public record StoreMemoryRequest(string? Content, string? Tags);
public record RunToolRequest(string? Tool, string? Input);
public record SetupRequest(SetupLlmRequest? Llm, SetupSafetyRequest? Safety, SetupMeshRequest? Mesh);
public record SetupLlmRequest(string? Provider, string? Model, string? Endpoint, string? ApiKey);
public record SetupSafetyRequest(bool SafeMode, bool ShellEnabled);
public record SetupMeshRequest(bool Enabled, int Port);

static class MaskHelper
{
    public static string? MaskKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 8) return key;
        return key[..4] + "****" + key[^4..];
    }
}

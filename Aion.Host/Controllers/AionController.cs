using Aion.Core;
using Aion.Core.Auth;
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
    private readonly IConversationStore _convStore;
    private readonly AuthService _auth;

    public AionController(
        AgentLoop agentLoop, ToolRegistry toolRegistry,
        IMemoryStore memory, IPlanStore planStore,
        IAionLogger logger, IRateLimiter rateLimiter,
        ISafetyGate safety, AppConfig config,
        IConversationStore convStore,
        AuthService auth)
    {
        _agentLoop = agentLoop;
        _toolRegistry = toolRegistry;
        _memory = memory;
        _planStore = planStore;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _safety = safety;
        _config = config;
        _convStore = convStore;
        _auth = auth;
    }

    // GET /api/health
    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var isFirstRun = await _auth.IsFirstRunAsync();

        return Ok(new
        {
            version = "1.0.0",
            name = "AION",
            status = "ok",
            uptime = Environment.TickCount64 / 1000,
            agents = 0,
            llm_status = _config.Llm.Provider,
            errors_1h = 0,
            first_run = isFirstRun
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

    // POST /api/conversations — create a new conversation
    [HttpPost("conversations")]
    public async Task<IActionResult> CreateConversation([FromBody] CreateConversationRequest req)
    {
        var conv = await _convStore.CreateConversationAsync(
            req.Title ?? "New Conversation", req.AgentId ?? "default", req.UserId, req.Model);
        return Ok(new { ok = true, conversation = conv });
    }

    // GET /api/conversations — list all
    [HttpGet("conversations")]
    public async Task<IActionResult> ListConversations([FromQuery] string? agent_id, [FromQuery] int limit = 50)
    {
        var list = await _convStore.ListConversationsAsync(agent_id, limit);
        return Ok(new { ok = true, conversations = list });
    }

    // GET /api/conversations/{id} — get one
    [HttpGet("conversations/{id}")]
    public async Task<IActionResult> GetConversation(string id)
    {
        var conv = await _convStore.GetConversationAsync(id);
        if (conv == null) return NotFound(new { ok = false, error = "Conversation not found" });
        return Ok(new { ok = true, conversation = conv });
    }

    // DELETE /api/conversations/{id}
    [HttpDelete("conversations/{id}")]
    public async Task<IActionResult> DeleteConversation(string id)
    {
        await _convStore.DeleteConversationAsync(id);
        return Ok(new { ok = true });
    }

    // POST /api/conversations/{id}/pin
    [HttpPost("conversations/{id}/pin")]
    public async Task<IActionResult> PinConversation(string id)
    {
        await _convStore.PinConversationAsync(id, true);
        return Ok(new { ok = true });
    }

    // POST /api/conversations/{id}/unpin
    [HttpPost("conversations/{id}/unpin")]
    public async Task<IActionResult> UnpinConversation(string id)
    {
        await _convStore.PinConversationAsync(id, false);
        return Ok(new { ok = true });
    }

    // GET /api/conversations/{id}/messages — get message history
    [HttpGet("conversations/{id}/messages")]
    public async Task<IActionResult> GetMessages(string id, [FromQuery] int limit = 100)
    {
        var msgs = await _convStore.GetMessagesAsync(id, limit);
        return Ok(new { ok = true, messages = msgs });
    }

    // PUT /api/messages/{id} — edit a message
    [HttpPut("messages/{id}")]
    public async Task<IActionResult> EditMessage(string id, [FromBody] EditMessageRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.Content))
            return BadRequest(new { ok = false, error = "Content required" });
        await _convStore.EditMessageAsync(id, req.Content);
        return Ok(new { ok = true });
    }

    // DELETE /api/messages/{id}
    [HttpDelete("messages/{id}")]
    public async Task<IActionResult> DeleteMessage(string id)
    {
        await _convStore.DeleteMessageAsync(id);
        return Ok(new { ok = true });
    }

    // POST /api/agents/{id}/message — send with conversation persistence
    [HttpPost("agents/{id}/message")]
    public async Task<IActionResult> SendMessage(string id, [FromBody] MessageRequest req)
    {
        var rateCheck = await _rateLimiter.CheckAsync("user", "tool_calls");
        if (!rateCheck.Allowed)
            return StatusCode(429, new { ok = false, error = rateCheck.Error, error_code = "RATE_LIMITED" });

        // Find or create conversation
        Conversation conv;
        if (!string.IsNullOrEmpty(req.ConversationId))
        {
            var existing = await _convStore.GetConversationAsync(req.ConversationId);
            if (existing != null) conv = existing;
            else conv = await _convStore.CreateConversationAsync(req.Text?[..Math.Min(req.Text.Length, 60)] ?? "Chat", id, "user", req.Model);
        }
        else
        {
            conv = await _convStore.CreateConversationAsync(req.Text?[..Math.Min(req.Text.Length, 60)] ?? "Chat", id, "user", req.Model);
        }

        // Store user message
        await _convStore.AddMessageAsync(conv.Id, "user", req.Text ?? "", req.Model);

        // Send to agent
        var request = new AgentRequest(id, "user", req.Text ?? "", req.Mode ?? "chat", req.Model);
        var result = await _agentLoop.RunAsync(request);

        // Store assistant reply
        var replyContent = result.Reply ?? result.Error ?? "I had trouble processing that. Could you clarify what you need?";
        if (!result.Success && result.Error != null)
        {
            replyContent = "I'm not sure I understood correctly. Could you rephrase or clarify what you're looking for?";
        }
        await _convStore.AddMessageAsync(conv.Id, "assistant", replyContent, req.Model);

        // Update title on first message
        var msgs = await _convStore.GetMessagesAsync(conv.Id, 2);
        if (msgs.Count <= 2)
        {
            conv.Title = req.Text?.Length > 60 ? req.Text[..57] + "..." : req.Text ?? "Chat";
            await _convStore.UpdateConversationAsync(conv);
        }

        return Ok(new { ok = result.Success || true, reply = replyContent, conversation_id = conv.Id });
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

    // POST /api/tools/create — register a dynamic tool (agent-created)
    [HttpPost("tools/create")]
    public IActionResult CreateTool([FromBody] CreateToolRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.Name) || string.IsNullOrWhiteSpace(req?.Code))
            return BadRequest(new { ok = false, error = "'name' and 'code' are required" });

        if (_toolRegistry.Contains(req.Name))
            return Conflict(new { ok = false, error = $"Tool '{req.Name}' already exists" });

        var sandbox = HttpContext.RequestServices.GetRequiredService<ISandboxExecutor>();
        _toolRegistry.RegisterDynamic(req.Name, req.Description ?? $"Agent-created tool: {req.Name}", req.Code, req.Language ?? "python", sandbox);
        _logger.Info("Tools", $"Dynamic tool registered: {req.Name} ({req.Language ?? "python"})", data: new { name = req.Name, description = req.Description });
        return Ok(new { ok = true, name = req.Name });
    }

    // DELETE /api/tools/{name} — remove a dynamic tool
    [HttpDelete("tools/{name}")]
    public IActionResult DeleteTool(string name)
    {
        if (_toolRegistry.Unregister(name))
        {
            _logger.Info("Tools", $"Tool unregistered: {name}");
            return Ok(new { ok = true, deleted = name });
        }
        return NotFound(new { ok = false, error = $"Tool '{name}' not found" });
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

    /// Auto-login: generates a token without requiring a password.
    /// Stores the token in ~/.aion/aion-token.txt for other scripts to use.
    [HttpGet("auto-login")]
    public async Task<IActionResult> AutoLogin()
    {
        var token = await _auth.AutoLoginAsync();

        // Write token to a file in the aion config dir so scripts can read it
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aion");
        Directory.CreateDirectory(configDir);
        var tokenFile = Path.Combine(configDir, "aion-token.txt");
        await System.IO.File.WriteAllTextAsync(tokenFile, token);

        _logger.Info("Auth", "Auto-login: token generated and saved");
        return Ok(new { ok = true, token, path = tokenFile });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.Username) || string.IsNullOrWhiteSpace(req?.Password))
            return BadRequest(new { ok = false, error = "Username and password required" });

        var (success, token, error) = await _auth.LoginAsync(req.Username, req.Password);
        if (!success || token == null)
            return Unauthorized(new { ok = false, error = error ?? "Login failed" });

        return Ok(new { ok = true, token });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        var token = authHeader?.Replace("Bearer ", "");
        if (token != null)
            await _auth.LogoutAsync(token);
        return Ok(new { ok = true });
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile uploadedFile)
    {
        if (uploadedFile == null || uploadedFile.Length == 0)
            return BadRequest(new { ok = false, error = "No file provided" });

        var uploadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aion", "uploads");
        Directory.CreateDirectory(uploadDir);

        var fileName = $"{Guid.NewGuid()}_{uploadedFile.FileName}";
        var filePath = Path.Combine(uploadDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await uploadedFile.CopyToAsync(stream);
        }

        // Read first 10KB for preview
        string preview;
        try
        {
            preview = await System.IO.File.ReadAllTextAsync(filePath);
            if (preview.Length > 10000)
            {
                preview = preview[..10000] + "\n... [truncated, full file saved]";
            }
        }
        catch
        {
            preview = $"File saved ({uploadedFile.Length} bytes). Binary or non-UTF8 content.";
        }

        _logger.Info("Upload", $"File uploaded: {uploadedFile.FileName} ({uploadedFile.Length} bytes)", data: new { file = fileName, size = uploadedFile.Length });

        return Ok(new { ok = true, file = fileName, name = uploadedFile.FileName, size = uploadedFile.Length, preview });
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var apiKey = await _auth.GetApiKeyAsync();
        return Ok(new { ok = true, settings = new { api_key = apiKey != null ? apiKey[..4] + "****" : null } });
    }

    [HttpPost("settings")]
    public async Task<IActionResult> SetSettings([FromBody] Dictionary<string, string> settings)
    {
        if (settings.TryGetValue("api_key", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
            await _auth.SetApiKeyAsync(apiKey);
        return Ok(new { ok = true });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        var token = authHeader?.Replace("Bearer ", "");
        if (token == null)
            return Unauthorized(new { ok = false, error = "Not authenticated" });

        var result = await _auth.ChangePasswordAsync(token, req?.OldPassword ?? "", req?.NewPassword ?? "");
        if (!result)
            return BadRequest(new { ok = false, error = "Failed to change password. Check your current password." });

        return Ok(new { ok = true, message = "Password changed successfully" });
    }
}

public record CreateToolRequest(string? Name, string? Description, string? Code, string? Language);
public record LoginRequest(string? Username, string? Password);
public record ChangePasswordRequest(string? OldPassword, string? NewPassword);
public record MessageRequest(string? Text, string? Mode, string? Model, string? ConversationId = null);
public record TaskRequest(string? Text, string? Priority, string? Model = null);
public record StoreMemoryRequest(string? Content, string? Tags);
public record RunToolRequest(string? Tool, string? Input);
public record SetupRequest(SetupLlmRequest? Llm, SetupSafetyRequest? Safety, SetupMeshRequest? Mesh);
public record SetupLlmRequest(string? Provider, string? Model, string? Endpoint, string? ApiKey);
public record SetupSafetyRequest(bool SafeMode, bool ShellEnabled);
public record SetupMeshRequest(bool Enabled, int Port);
public record CreateConversationRequest(string? Title, string? AgentId, string? UserId, string? Model);
public record EditMessageRequest(string? Content);

static class MaskHelper
{
    public static string? MaskKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 8) return key;
        return key[..4] + "****" + key[^4..];
    }
}

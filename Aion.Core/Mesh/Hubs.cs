using System.Collections.Concurrent;
using Aion.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Aion.Core.Mesh;

public class MeshHub : Hub
{
    private static readonly ConcurrentDictionary<string, AgentRegistration> _agents = new();
    private readonly IAionLogger _logger;

    public MeshHub(IAionLogger logger) { _logger = logger; }

    public async Task Register(AgentRegistration reg)
    {
        _agents[Context.ConnectionId] = reg;
        await Groups.AddToGroupAsync(Context.ConnectionId, "#general");

        var rooms = reg.Rooms ?? new List<string> { "#general" };
        foreach (var room in rooms)
            await Groups.AddToGroupAsync(Context.ConnectionId, room);

        await Clients.Group("#general").SendAsync("agent_status", new
        {
            agent_id = reg.AgentId,
            display_name = reg.DisplayName,
            status = "online",
            capabilities = (object?)reg.Capabilities ?? new List<string>()
        });

        await Clients.Caller.SendAsync("welcome", new
        {
            agent_id = reg.AgentId,
            session_token = Guid.NewGuid().ToString(),
            server_version = "1.0.0",
            rooms = rooms,
            missed_messages = Array.Empty<object>()
        });

        _logger.Info("Mesh", $"Agent '{reg.DisplayName}' registered via WebSocket", reg.AgentId);
    }

    public async Task SendMessage(MeshMessageDto msg)
    {
        if (!string.IsNullOrEmpty(msg.To) && msg.To.StartsWith("#"))
        {
            await Clients.Group(msg.To).SendAsync("broadcast", new
            {
                room = msg.To,
                from = GetAgentId(),
                id = msg.Id,
                timestamp = DateTime.UtcNow.ToString("O"),
                body = msg.Body
            });
        }
        else if (!string.IsNullOrEmpty(msg.To))
        {
            var targetConn = _agents.FirstOrDefault(x => x.Value.AgentId == msg.To).Key;
            if (targetConn != null)
            {
                await Clients.Client(targetConn).SendAsync("deliver", new
                {
                    from = GetAgentId(),
                    to = msg.To,
                    id = msg.Id,
                    body = msg.Body
                });
            }
        }
    }

    public async Task JoinRoom(string room)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
    }

    public async Task LeaveRoom(string room)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
    }

    public async Task UpdateStatus(StatusUpdate update)
    {
        if (_agents.TryGetValue(Context.ConnectionId, out var reg))
        {
            await Clients.Group("#general").SendAsync("agent_status", new
            {
                agent_id = reg.AgentId,
                status = update.Status,
                current_task = update.CurrentTask
            });
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_agents.TryRemove(Context.ConnectionId, out var reg))
        {
            await Clients.Group("#general").SendAsync("agent_status", new
            {
                agent_id = reg.AgentId,
                status = "offline"
            });
            _logger.Warn("Mesh", $"Agent '{reg.DisplayName}' disconnected", reg.AgentId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    private string GetAgentId()
    {
        return _agents.TryGetValue(Context.ConnectionId, out var reg) ? reg.AgentId ?? "unknown" : "unknown";
    }
}

public class DashboardHub : Hub
{
    public async Task Subscribe(string topic)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"dashboard:{topic}");
    }
}

public record AgentRegistration(string? AgentId, string? DisplayName, List<string>? Capabilities,
    int CapabilityLevel, string Status, List<string>? Rooms);

public record MeshMessageDto(string? To, string? Id, string? InReplyTo, object? Body, string Priority = "normal");

public record StatusUpdate(string Status, string? CurrentTask);

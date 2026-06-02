namespace Aion.Core.Interfaces;

public record MeshMessage(string Type, string From, string To, string Id,
    string? InReplyTo, DateTime Timestamp, object? Body, string Priority = "normal");

public record AgentInfo(string AgentId, string DisplayName, string Status,
    List<string> Capabilities, List<string> Rooms, DateTime LastSeen);

public interface IMessageBus
{
    Task PublishAsync(MeshMessage message);
    Task SubscribeAsync(string room, Func<MeshMessage, Task> handler);
    Task UnsubscribeAsync(string room);
    Task<List<AgentInfo>> GetConnectedAgentsAsync();
    Task<bool> IsAgentOnlineAsync(string agentId);
}

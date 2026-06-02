namespace Aion.Core.Interfaces;

public record AgentRequest(string AgentId, string UserId, string Input, string Mode);
public record AgentResult(bool Success, string? Reply, string? PlanId, string? Error);

public interface IAgentLoop
{
    Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default);
}

namespace Aion.Core.Interfaces;

public record PlanStep(int Step, string Tool, string Input, string? Result,
    string Status, int Retries, DateTime? CompletedAt, string? Error);

public record Plan(string Id, string SessionId, string AgentId, string UserId,
    DateTime CreatedAt, string Status, int CurrentStep, int TotalSteps,
    int RetryCount, string? Error, List<PlanStep> Steps);

public interface IPlanStore
{
    Task<Plan> CreateAsync(Plan plan);
    Task<Plan?> GetAsync(string planId);
    Task<bool> UpdateStepAsync(string planId, int step, string status, string? result, string? error);
    Task<bool> UpdateStatusAsync(string planId, string status, string? error);
    Task<List<Plan>> GetActivePlansAsync(string agentId);
}

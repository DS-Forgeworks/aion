namespace Aion.Core.Interfaces;

public record SafetyDecision(bool Allowed, string? DenyReason, ToolCapability RequiredLevel);

public interface ISafetyGate
{
    Task<SafetyDecision> EvaluateAsync(string toolName, string input, ToolCapability toolLevel,
        ToolCapability agentLevel);
}

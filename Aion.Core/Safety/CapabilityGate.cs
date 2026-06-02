using Aion.Core.Interfaces;

namespace Aion.Core.Safety;

public class CapabilityGate : ISafetyGate
{
    private static readonly Dictionary<string, string> DeniedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        { "rm -rf", "Recursive delete blocked" },
        { "dd if=", "Raw block write blocked" },
        { "mkfs", "Filesystem format blocked" },
        { ":(){ :|:& };:", "Fork bomb blocked" },
    };

    private static readonly string[] DeniedExtensions = { ".key", ".pem", ".env" };

    public Task<SafetyDecision> EvaluateAsync(string toolName, string input, ToolCapability toolLevel, ToolCapability agentLevel)
    {
        // Capability level check
        if ((int)toolLevel > (int)agentLevel)
        {
            return Task.FromResult(new SafetyDecision(false,
                $"Tool '{toolName}' requires level {(int)toolLevel}, agent has level {(int)agentLevel}",
                toolLevel));
        }

        // Command deny list (for shell_command tool)
        if (toolName == "shell_command")
        {
            foreach (var (pattern, reason) in DeniedCommands)
            {
                if (input.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new SafetyDecision(false,
                        $"Command blocked: {reason}", toolLevel));
                }
            }
        }

        // File path scanning
        if (toolName.StartsWith("file_", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var ext in DeniedExtensions)
            {
                if (input.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ||
                    input.Contains(ext + "\"", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new SafetyDecision(false,
                        $"File access blocked: '{ext}' files are restricted", toolLevel));
                }
            }

            // Path traversal check
            if (input.Contains("..", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new SafetyDecision(false,
                    "Path traversal detected", toolLevel));
            }
        }

        return Task.FromResult(new SafetyDecision(true, null, toolLevel));
    }
}

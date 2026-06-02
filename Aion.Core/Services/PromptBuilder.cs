using Aion.Core.Interfaces;

namespace Aion.Core.Services;

public class PromptBuilder
{
    public string BuildSystemPrompt(
        string agentName,
        string soulContent,
        string toolDefinitions,
        int capabilityLevel,
        string recentMemory,
        string longTermMemory)
    {
        return $@"You are {agentName}.
{soulContent}

╔══════════════════════════════════════════════════════╗
║  CAPABILITIES                                       ║
╠══════════════════════════════════════════════════════╣
║ Available tools:                                    ║
║ {toolDefinitions.Replace("\n", "\n║ ")} 
║                                                     ║
║ Safety level: {capabilityLevel}                        ║
║                                                     ║
║ ⚠️  Rules:                                          ║
║  - Return ONLY a JSON array. No explanations.        ║
║  - Each element is a tool call or an answer.         ║
║  - If you cannot complete the request, use           ║
║    {{""tool"": ""none"", ""input"": {{""error"": reason}}}}.         ║
║  - Do NOT fabricate tool results.                    ║
╚══════════════════════════════════════════════════════╝

--- MEMORY ---
{recentMemory}
--- END MEMORY ---

--- LONG-TERM MEMORY ---
{longTermMemory}
--- END LONG-TERM MEMORY ---

Now respond to the user's request.";
    }

    public string BuildToolResultInjection(string toolName, string result)
    {
        return $@"
--- TOOL RESULT ---
{toolName} returned:
{result}
--- END TOOL RESULT ---

Now continue. If this answers the user, return an answer. If you need more tools, return more tool calls.";
    }

    public string BuildRetryPrompt(string originalInput, string errorMessage)
    {
        return $@"Your previous response had issues:
{errorMessage}

Original input was:
{originalInput}

Fix ONLY these issues and retry. Return ONLY a valid JSON array.";
    }

    public string BuildFinalAttemptPrompt(string context)
    {
        var escaped = EscapeJson(context);
        return $"[{{\"tool\":\"none\",\"input\":{{\"answer\":\"{escaped}\"}}}}]";
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");
    }
}

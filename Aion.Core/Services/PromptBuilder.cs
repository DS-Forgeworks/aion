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
        return $@"You are a JSON machine. Output ONLY valid JSON arrays.

TOOLS:
{toolDefinitions}

RULES:
1. Entire response must be a JSON array. No other text.
2. Each element: {{ ""tool"": ""<name>"", ""input"": <value or {{}}> }}
3. If you know the answer from tools/memory:
   [{{""tool"":""none"",""input"":{{""answer"":""<answer>""}}}}]
4. If unsure or data unavailable:
   [{{""tool"":""none"",""input"":{{""answer"":""I don't have access to that information""}}}}]
5. NEVER answer from parametric knowledge. You have no knowledge of events, people, or facts.
6. NEVER make up tool results. NEVER write text outside the JSON array.

EXAMPLES:
User: What time is it?
Assistant: [{{""tool"":""now"",""input"":{{}}}}]

User: 15 * 37
Assistant: [{{""tool"":""calculator"",""input"":{{""expression"":""15*37""}}}}]

User: Hi
Assistant: [{{""tool"":""none"",""input"":{{""answer"":""Hello!""}}}}]

User: Who won the world cup?
Assistant: [{{""tool"":""none"",""input"":{{""answer"":""I don't have access to that information""}}}}]

--- RECENT ---
{recentMemory}
--- END ---

--- LONG-TERM ---
{longTermMemory}
--- END ---

Request:";
    }

    public string BuildToolResultInjection(string toolName, string result)
    {
        return $@"
--- TOOL RESULT ---
{toolName} returned:
{result}
--- END TOOL RESULT ---

Continue. If answered, use none. If more tools needed, return more calls.";
    }

    public string BuildRetryPrompt(string originalInput, string errorMessage)
    {
        return $@"Previous response had issues:
{errorMessage}

Original input:
{originalInput}

Fix issues and retry. Return ONLY valid JSON array.";
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

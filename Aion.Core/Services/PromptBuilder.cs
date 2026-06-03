using Aion.Core.Interfaces;

namespace Aion.Core.Services;

public class PromptBuilder
{
    private string? _soulContent;
    private string? _protocolContent;

    /// Load the soul file (identity, values, voice)
    public void LoadSoul(string path)
    {
        if (File.Exists(path))
            _soulContent = File.ReadAllText(path);
    }

    /// Load the protocol file (JSON format rules, tool structure)
    public void LoadProtocol(string path)
    {
        if (File.Exists(path))
            _protocolContent = File.ReadAllText(path);
    }

    public string BuildSystemPrompt(
        string agentName,
        string soulContent,
        string toolDefinitions,
        int capabilityLevel,
        string recentMemory,
        string longTermMemory)
    {
        var soul = _soulContent ?? soulContent;
        if (string.IsNullOrWhiteSpace(soul))
            soul = "You are AION, a capable AI agent. Be helpful, concise, and warm.";

        var protocol = _protocolContent ?? @"You MUST output ONLY valid JSON arrays.

FORMAT: [{""tool"":""<name>"",""input"":<value>}]
If answering: [{""tool"":""none"",""input"":{""answer"":""<response>""}}]

RULES:
1. Entire response must be a JSON array. No other text.
2. NEVER answer from parametric knowledge. Use tools.
3. NEVER make up tool results.";

        return $@"{soul}

---

{protocol}

---

TOOLS:
{toolDefinitions}

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

using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.Core.Interfaces;
using Aion.Core.Repair;

namespace Aion.Core.Planning;

/// <summary>
/// Extracts and validates JSON plans from LLM output.
/// Mirrors Halcyon's PlanExtractor.py but in C# with JsonRepairPipeline integration.
/// </summary>
public class PlanExtractor
{
    private readonly IJsonRepairer _repairer;
    private readonly IAionLogger _logger;

    public PlanExtractor(IJsonRepairer repairer, IAionLogger logger)
    {
        _repairer = repairer;
        _logger = logger;
    }

    /// <summary>
    /// Extract a plan from LLM output. Returns a list of steps (min length 1).
    /// Falls back to [{"tool":"none","input":{"answer":"[Error]..."}}] on failure.
    /// </summary>
    public List<PlanStep> ExtractPlan(string text, Func<string, Task<string>>? llmRetry = null)
    {
        text = text.Trim();
        _logger.Debug("PlanExtractor", $"Extracting plan from: {text[..Math.Min(text.Length, 200)]}...");

        // 1. Try extracting JSON array from any surrounding text
        var jsonBlock = ExtractJsonBlock(text);
        var parsed = jsonBlock != null ? ParseJson(jsonBlock) : null;

        // 2. Fallback: try parsing the raw text directly
        parsed ??= ParseJson(text);

        // 3. LLM retry on parse failure
        if (parsed == null && llmRetry != null)
        {
            var retryTask = llmRetry.Invoke(
                $"Your previous response contained invalid JSON:\n\n{text[..Math.Min(text.Length, 500)]}\n\n" +
                "Respond with ONLY a valid JSON array. Each element must have \"tool\" and \"input\".\n" +
                "Example: [{\"tool\":\"none\",\"input\":{\"answer\":\"Simple text answer\"}}]\n" +
                "Generate ONLY the JSON array:");
            var retryOutput = retryTask.GetAwaiter().GetResult();
            return ExtractPlan(retryOutput, llmRetry: null); // prevent infinite recursion
        }

        // 4. Validate steps
        if (parsed != null && parsed.Count > 0)
        {
            var validSteps = parsed.Where(s => !string.IsNullOrWhiteSpace(s.Tool) && s.Tool != "none" || s.Input != null).ToList();
            if (validSteps.Count > 0)
            {
                _logger.Debug("PlanExtractor", $"Extracted {validSteps.Count} valid steps");
                return validSteps;
            }

            // If we have only a "none" step with an answer, wrap and return
            var noneStep = parsed.FirstOrDefault(s => s.Tool == "none");
            if (noneStep != null)
                return new List<PlanStep> { noneStep };
        }

        // 5. Try using the repair pipeline as last resort
        var repaired = _repairer.Repair(text, forceHeavy: true);
        if (repaired.Success && !string.IsNullOrWhiteSpace(repaired.Json))
        {
            var repairParsed = ParseJson(repaired.Json);
            if (repairParsed != null && repairParsed.Count > 0)
            {
                _logger.Debug("PlanExtractor", $"Repair pipeline recovered {repairParsed.Count} steps");
                return repairParsed;
            }
        }

        // 6. Final fallback
        _logger.Warn("PlanExtractor", "Could not extract valid plan, using fallback");
        return new List<PlanStep>
        {
            new PlanStep("none", JsonSerializer.Serialize(new { answer = "[Error] Could not extract valid JSON" }))
        };
    }

    /// <summary>
    /// Extract the first JSON array from text, stripping code fences and surrounding text.
    /// </summary>
    private static string? ExtractJsonBlock(string text)
    {
        // Remove code fences
        var cleaned = Regex.Replace(text, @"```json|```", "", RegexOptions.IgnoreCase).Trim();

        // Non-greedy JSON array match
        var match = Regex.Match(cleaned, @"(\[.*?\])", RegexOptions.Singleline);
        if (match.Success)
            return match.Groups[1].Value;

        return null;
    }

    /// <summary>
    /// Parse a JSON string into a list of PlanSteps.
    /// Handles single-object wrapping and extraction of answer from "none" steps.
    /// </summary>
    private static List<PlanStep>? ParseJson(string jsonStr)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            List<JsonElement> elements;

            if (root.ValueKind == JsonValueKind.Array)
                elements = root.EnumerateArray().ToList();
            else if (root.ValueKind == JsonValueKind.Object)
                elements = new List<JsonElement> { root };
            else
                return null;

            var steps = new List<PlanStep>();
            foreach (var el in elements)
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                if (!el.TryGetProperty("tool", out var toolEl)) continue;
                var tool = toolEl.GetString();
                if (string.IsNullOrWhiteSpace(tool)) continue;

                string? inputStr = null;
                if (el.TryGetProperty("input", out var inputEl))
                {
                    inputStr = inputEl.ValueKind switch
                    {
                        JsonValueKind.String => inputEl.GetString(),
                        JsonValueKind.Object when inputEl.TryGetProperty("answer", out var ans) => ans.GetString() ?? inputEl.GetRawText(),
                        JsonValueKind.Object => inputEl.GetRawText(),
                        _ => inputEl.GetRawText()
                    };
                }

                steps.Add(new PlanStep(tool, inputStr));
            }

            return steps.Count > 0 ? steps : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// A single step in a plan: a tool name and its input.
/// </summary>
public record PlanStep(string Tool, string? Input);

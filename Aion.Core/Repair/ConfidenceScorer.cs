using Aion.Core.Interfaces;

namespace Aion.Core.Repair;

public class ConfidenceScorer : IConfidenceScorer
{
    public ConfidenceScore Score(string rawInput, string parsedJson, string? toolName)
    {
        double score = 0.0;
        var signals = new List<string>();

        // Signal 1: JSON parsed cleanly
        try
        {
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(parsedJson);
            score += 0.30;
            signals.Add("JSON parsed cleanly (+0.30)");
        }
        catch
        {
            score -= 0.20;
            signals.Add("JSON parse failed (-0.20)");
        }

        // Signal 2: Tool in registry
        if (!string.IsNullOrEmpty(toolName) && toolName != "none" && !toolName.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.20;
            signals.Add($"Tool '{toolName}' recognized (+0.20)");
        }

        // Signal 3: Not truncated
        if (parsedJson.Length < 4000)
        {
            score += 0.15;
            signals.Add("Output not truncated (+0.15)");
        }
        else
        {
            score -= 0.05;
            signals.Add("Output near max length (-0.05)");
        }

        // Signal 4: Input has parameters
        if (rawInput.Contains("input", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.10;
            signals.Add("Input parameters present (+0.10)");
        }

        // Signal 5: "I don't know" patterns
        if (RegexContains(rawInput, @"(don't know|cannot|unable|not sure|confused)"))
        {
            score -= 0.25;
            signals.Add("Uncertainty patterns detected (-0.25)");
        }

        // Signal 6: Apology patterns
        if (RegexContains(rawInput, @"(sorry|apologize|my mistake)"))
        {
            score -= 0.15;
            signals.Add("Apology patterns detected (-0.15)");
        }

        // Signal 7: Contains tool call structure
        if (parsedJson.Contains("\"tool\"") || parsedJson.Contains("\"input\""))
        {
            score += 0.10;
            signals.Add("Tool call structure detected (+0.10)");
        }

        score = Math.Clamp(score, 0.0, 1.0);
        return new ConfidenceScore(score, signals);
    }

    private static bool RegexContains(string input, string pattern)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

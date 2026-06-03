using System.Text.RegularExpressions;
using Aion.Core.Interfaces;

namespace Aion.Core.Repair;

public class JsonRepairPipeline : IJsonRepairer
{
    public ParseResult Repair(string rawOutput, bool forceHeavy = false)
    {
        int stepsUsed = 0;
        var text = rawOutput.Trim();

        // Step 1: Strip markdown fences
        text = Regex.Replace(text, @"^```(?:json)?\s*\n?", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"\n```\s*$", "", RegexOptions.Multiline);
        stepsUsed++;

        // Step 2: Extract first JSON array (prefer), or object (wrap in array)
        var arrayMatch = Regex.Match(text, @"(\[[\s\S]*?\])");
        if (!arrayMatch.Success)
            arrayMatch = Regex.Match(text, @"(\{[\s\S]*?\})");
        if (arrayMatch.Success)
        {
            text = arrayMatch.Groups[1].Value.Trim();
            // If we got an object instead of an array, wrap it
            if (text.StartsWith("{") && !text.StartsWith("["))
                text = $"[{text}]";
        }
        stepsUsed++;

        // Step 3: Try direct parse
        try
        {
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(text);
            return new ParseResult(true, text, null, stepsUsed, false);
        }
        catch { }

        // Step 4: Replace single quotes with double quotes
        text = Regex.Replace(text, @"'(?<key>[^']*?)':", "\"${key}\":");
        text = Regex.Replace(text, @":\s*'(?<val>[^']*?)'", ":\"${val}\"");
        stepsUsed++;
        try { System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(text); return new ParseResult(true, text, null, stepsUsed, false); } catch { }

        var isHeavy = forceHeavy;

        // Step 5: Fix unescaped strings
        text = text.Replace("\r\n", "\\n").Replace("\n", "\\n");
        text = Regex.Replace(text, @"(?<!\\)""(?!,|:|\}|\])", "\\\"");
        stepsUsed++;

        // Step 6: Balance brackets
        text = BalanceBrackets(text);
        stepsUsed++;

        // Step 7: Remove trailing commas
        text = Regex.Replace(text, @",\s*([}\]])", "$1");
        stepsUsed++;

        try
        {
            System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(text);
            return new ParseResult(true, text, null, stepsUsed, true);
        }
        catch (Exception ex)
        {
            return new ParseResult(false, text, $"Repair failed after {stepsUsed} steps: {ex.Message}", stepsUsed, isHeavy);
        }
    }

    private string BalanceBrackets(string text)
    {
        int openBraces = text.Count(c => c == '{');
        int closeBraces = text.Count(c => c == '}');
        int openBrackets = text.Count(c => c == '[');
        int closeBrackets = text.Count(c => c == ']');

        while (openBraces > closeBraces) { text += "}"; closeBraces++; }
        while (closeBraces > openBraces) { text = "{" + text; openBraces++; }
        while (openBrackets > closeBrackets) { text += "]"; closeBrackets++; }
        while (closeBrackets > openBrackets) { text = "[" + text; openBrackets++; }

        return text;
    }
}

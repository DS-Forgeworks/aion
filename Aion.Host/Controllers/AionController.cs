static class ReplyHelper
{
    private static readonly System.Text.RegularExpressions.Regex _toolArrayRegex = new(
        @"\[\s*\{\s*""tool""\s*:\s*""none""\s*,\s*""input""\s*:\s*\{\s*""answer""\s*:\s*""(.*?)""\s*\}\s*\}\s*\]",
        System.Text.RegularExpressions.RegexOptions.Singleline);

    /// Strip JSON, thinking model artifacts, tool framework output from agent replies
    public static string StripJsonFromReply(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        text = text.Trim();

        // 0. Extract from tool response array: [{"tool":"none","input":{"answer":"..."}}]
        var match = _toolArrayRegex.Match(text);
        if (match.Success)
        {
            var extracted = match.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(extracted))
                return extracted;
        }

        // 1. Remove   ...    blocks (thinking model output)
        // Handle both <think> tags and plain  tags
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"<think>.*?</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline).Trim();

        // Also handle unclosed <think>
        var thinkStart = text.IndexOf("<think>");
        if (thinkStart >= 0)
            text = text[..thinkStart].Trim();

        // Handle plain  tags
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"    .*?    ", "", System.Text.RegularExpressions.RegexOptions.Singleline).Trim();

        var plainStart = text.IndexOf("    ");
        if (plainStart >= 0)
        {
            var plainEnd = text.IndexOf("    ", plainStart + 3);
            if (plainEnd >= 0)
                text = (text[..plainStart] + text[(plainEnd + 3)..]).Trim();
            else
                text = text[..plainStart].Trim();
        }

        // 2. Remove markdown code fences
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^```(?:json)?\s*\n?", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n?```$", "");

        // 3. If the entire thing is a JSON object/array, try to extract meaningful content
        if ((text.StartsWith("{") && text.EndsWith("}")) ||
            (text.StartsWith("[") && text.EndsWith("]")))
        {
            // Try to find any "answer" or "text" or "content" field in it
            var answerMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"""answer""\s*:\s*""(.+?)(?<!\\)""", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (answerMatch.Success)
                return answerMatch.Groups[1].Value;

            var textMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"""text""\s*:\s*""(.+?)(?<!\\)""", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (textMatch.Success)
                return textMatch.Groups[1].Value;

            var contentMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"""content""\s*:\s*""(.+?)(?<!\\)""", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (contentMatch.Success)
                return contentMatch.Groups[1].Value;

            return "Got it. What's next?";
        }

        // 4. Remove tool action prefixes
        string[] prefixes = { "[TOOL_CALL]", "[PLAN]", "[MEMORY]", "[ERROR]", "Tool call:", "Calling tool:" };
        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        return string.IsNullOrWhiteSpace(text) ? "Got it. What's next?" : text;
    }
}

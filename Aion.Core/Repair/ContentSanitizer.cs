using Aion.Core.Interfaces;

namespace Aion.Core.Repair;

public class ContentSanitizer : IContentSanitizer
{
    public async Task<SanitizedContent> SanitizeAsync(string content, ContentType type)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var originalLength = content.Length;
        var result = content;

        // Whitespace normalisation
        result = result.Replace("\r\n", "\n");
        result = RegexReplace(result, @"\t", "  ");

        // Strip trailing whitespace per line
        result = RegexReplace(result, @"[ \t]+\n", "\n");
        result = result.TrimEnd();

        // Collapse multiple blank lines to max 2
        result = RegexReplace(result, @"\n{3,}", "\n\n");

        switch (type)
        {
            case ContentType.ConfigFile:
                result = EscapeShellChars(result);
                warnings.Add("Shell characters escaped for config file");
                break;

            case ContentType.CodeBlock:
                var opens = result.Split("```").Length - 1;
                if (opens % 2 != 0)
                {
                    errors.Add("Unclosed code block detected");
                    result += "\n```";
                }
                break;

            case ContentType.FileWrite:
                var hasNull = result.Contains('\0');
                if (hasNull)
                {
                    errors.Add("Binary content detected in text write");
                    return new SanitizedContent(result, warnings, errors, false, originalLength);
                }
                break;

            case ContentType.Message:
                // Ensure valid JSON
                try
                {
                    System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(result);
                }
                catch
                {
                    warnings.Add("Message is not valid JSON");
                }
                break;
        }

        // Length limits
        int maxLen = type switch
        {
            ContentType.ConfigFile => 1_000_000,
            ContentType.Message => 1_000_000,
            ContentType.LogEntry => 100_000,
            ContentType.MemoryEntry => 10_000,
            _ => 10_000_000
        };

        if (result.Length > maxLen)
        {
            result = result[..maxLen];
            warnings.Add($"Content truncated from {originalLength} to {maxLen} bytes");
        }

        return new SanitizedContent(result, warnings, errors, result.Length < originalLength, originalLength);
    }

    private static string EscapeShellChars(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, @"(\$[A-Z_]+|`[^`]*`|\$\([^)]+\))", m => $"\\{m.Value}");
    }

    private static string RegexReplace(string input, string pattern, string replacement)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, pattern, replacement);
    }
}

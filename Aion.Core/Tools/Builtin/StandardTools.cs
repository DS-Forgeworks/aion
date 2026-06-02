using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.Core.Interfaces;

namespace Aion.Core.Tools.Builtin;

public class WebFetchTool : ITool
{
    public string Name => "web_fetch";
    public string Description => "Fetch a URL and return clean text content";
    public ToolCapability Capability => ToolCapability.ReadOnly;

    public async Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var url = ExtractUrl(input);
            if (string.IsNullOrEmpty(url))
                return ToolResult.Fail("No URL found in input");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AION/1.0");

            var response = await http.GetStringAsync(url, ct);
            if (string.IsNullOrEmpty(response))
                return ToolResult.Fail("Empty response from URL");

            // Strip HTML tags for clean text
            var clean = Regex.Replace(response, @"<[^>]+>", " ");
            clean = Regex.Replace(clean, @"\s+", " ");
            clean = clean[..Math.Min(clean.Length, 5000)]; // Max 5KB

            return ToolResult.Ok(clean.Trim());
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Fail("Request timed out after 15 seconds");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Fetch failed: {ex.Message}");
        }
    }

    private string ExtractUrl(string input)
    {
        var urlMatch = Regex.Match(input, @"https?://[^\s""'<>]+");
        if (urlMatch.Success)
            return urlMatch.Value;

        try
        {
            var parsed = JsonDocument.Parse(input);
            if (parsed.RootElement.TryGetProperty("url", out var urlProp))
                return urlProp.GetString() ?? "";
        }
        catch { }

        return input.Trim().StartsWith("http") ? input.Trim() : "";
    }
}

public class CalculatorTool : ITool
{
    public string Name => "calculator";
    public string Description => "Evaluate a mathematical expression";
    public ToolCapability Capability => ToolCapability.ReadOnly;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            // Extract expression from JSON or plain text
            var expr = ExtractExpression(input);
            if (string.IsNullOrEmpty(expr))
                return Task.FromResult(ToolResult.Fail("No expression found"));

            // Use a simple DataTable-based eval (safe, no reflection abuse)
            var dt = new System.Data.DataTable();
            var result = dt.Compute(expr, "");
            return Task.FromResult(ToolResult.Ok($"{result}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Calculation error: {ex.Message}"));
        }
    }

    private string ExtractExpression(string input)
    {
        try
        {
            var parsed = JsonDocument.Parse(input);
            if (parsed.RootElement.TryGetProperty("expression", out var expr))
                return expr.GetString() ?? "";
        }
        catch { }

        return input.Trim();
    }
}

public class NowTool : ITool
{
    public string Name => "now";
    public string Description => "Get the current date, time, and timezone";
    public ToolCapability Capability => ToolCapability.ReadOnly;

    public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.Now;
            var utc = DateTime.UtcNow;
            var tz = TimeZoneInfo.Local;
            var offset = tz.BaseUtcOffset;
            var sign = offset >= TimeSpan.Zero ? "+" : "-";
            var hours = Math.Abs(offset.Hours);
            var minutes = Math.Abs(offset.Minutes);

            var result = $"Local: {now:yyyy-MM-dd HH:mm:ss}\nUTC: {utc:yyyy-MM-dd HH:mm:ss}\nTimezone: {tz.DisplayName} (UTC{sign}{hours:D2}:{minutes:D2})";

            return Task.FromResult(ToolResult.Ok(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Time error: {ex.Message}"));
        }
    }
}

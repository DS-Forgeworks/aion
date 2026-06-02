using System.Text;
using System.Text.Json;
using Aion.Core.Interfaces;
using Aion.Core.Models;

namespace Aion.Core.Services;

public class LlmService
{
    private readonly HttpClient _http;
    private readonly AppConfig _config;
    private readonly IContentSanitizer _sanitizer;

    public string ProviderName => _config.Llm.Provider;
    public bool IsAvailable => !string.IsNullOrEmpty(_config.Llm.Endpoint) || _config.Llm.Provider == "ollama";

    public LlmService(AppConfig config, IContentSanitizer sanitizer)
    {
        _config = config;
        _sanitizer = sanitizer;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<string> GenerateAsync(LLMRequest request, CancellationToken ct = default)
    {
        var endpoint = GetEndpoint();
        var payload = BuildPayload(request);

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(endpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return ExtractText(responseBody);
    }

    private string GetEndpoint()
    {
        return _config.Llm.Provider switch
        {
            "openai" => "https://api.openai.com/v1/chat/completions",
            "deepseek" => "https://api.deepseek.com/chat/completions",
            "ollama" => $"{(string.IsNullOrEmpty(_config.Llm.Endpoint) ? "http://127.0.0.1:11434" : _config.Llm.Endpoint)}/v1/chat/completions",
            _ => _config.Llm.Endpoint ?? "http://127.0.0.1:11434/v1/chat/completions"
        };
    }

    private object BuildPayload(LLMRequest request)
    {
        return new
        {
            model = _config.Llm.Model ?? "qwen3:8b",
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserInput }
            },
            max_tokens = request.MaxTokens,
            temperature = request.Temperature
        };
    }

    private string ExtractText(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                return message.GetProperty("content").GetString() ?? "";
            }
        }
        catch { }

        return responseBody;
    }
}

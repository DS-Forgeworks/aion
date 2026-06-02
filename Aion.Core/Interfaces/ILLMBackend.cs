namespace Aion.Core.Interfaces;

public record LLMRequest(string SystemPrompt, string UserInput, int MaxTokens = 4096,
    double Temperature = 0.7, string? Model = null);

public interface ILLMBackend
{
    Task<string> GenerateAsync(LLMRequest request, CancellationToken ct = default);
    string ProviderName { get; }
    bool IsAvailable { get; }
}

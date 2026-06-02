using Aion.Core.Interfaces;
using Aion.Core.Repair;
using Aion.Core.Services;
using Aion.Core.Tools;
using Aion.Core.Safety;

namespace Aion.Core;

public class AgentLoop : IAgentLoop
{
    private readonly LlmService _llm;
    private readonly PromptBuilder _promptBuilder;
    private readonly ToolRegistry _toolRegistry;
    private readonly IJsonRepairer _repairer;
    private readonly IConfidenceScorer _scorer;
    private readonly ISafetyGate _safety;
    private readonly IMemoryStore _memory;
    private readonly IPlanStore _planStore;
    private readonly IContentSanitizer _sanitizer;
    private readonly IAionLogger _logger;
    private readonly string _agentName;
    private readonly int _capabilityLevel;
    private int _retryCount;

    public const int MaxRetries = 3;

    public AgentLoop(
        LlmService llm,
        PromptBuilder promptBuilder,
        ToolRegistry toolRegistry,
        IJsonRepairer repairer,
        IConfidenceScorer scorer,
        ISafetyGate safety,
        IMemoryStore memory,
        IPlanStore planStore,
        IContentSanitizer sanitizer,
        IAionLogger logger,
        string agentName = "luna",
        int capabilityLevel = 1)
    {
        _llm = llm;
        _promptBuilder = promptBuilder;
        _toolRegistry = toolRegistry;
        _repairer = repairer;
        _scorer = scorer;
        _safety = safety;
        _memory = memory;
        _planStore = planStore;
        _sanitizer = sanitizer;
        _logger = logger;
        _agentName = agentName;
        _capabilityLevel = capabilityLevel;
    }

    public async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        _retryCount = 0;
        _logger.Info("AgentLoop", $"Running request: {request.Input[..Math.Min(request.Input.Length, 100)]}", request.AgentId);

        try
        {
            // 1. Build prompt with context
            var recentMem = await _memory.GetRecentAsync(5);
            var recentStr = string.Join("\n", recentMem.Select(m => $"[{m.CreatedAt:HH:mm}] {m.Content[..Math.Min(m.Content.Length, 200)]}"));
            var tools = _toolRegistry.GetDefinitions();
            var toolStr = string.Join("\n", tools.Select(t => $"  - {t.Name}: {t.Description}"));

            var soulContent = "You are an AI assistant specialized in task automation and data processing.";

            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                _agentName, soulContent, toolStr, _capabilityLevel,
                recentStr, "");

            // 2-6. LLM call with retry loop
            while (_retryCount < MaxRetries)
            {
                _retryCount++;
                _logger.Debug("AgentLoop", $"LLM call attempt {_retryCount}/{MaxRetries}", request.AgentId);

                var llmRequest = new LLMRequest(systemPrompt, request.Input);
                string llmResponse;

                try
                {
                    llmResponse = await _llm.GenerateAsync(llmRequest, ct);
                }
                catch (Exception ex)
                {
                    _logger.Warn("AgentLoop", $"LLM call failed: {ex.Message}", request.AgentId, data: new { attempt = _retryCount });
                    if (_retryCount >= MaxRetries)
                        return new AgentResult(false, null, null, "LLM call failed after 3 retries");
                    continue;
                }

                // 3. Parse and repair
                var parsed = _repairer.Repair(llmResponse, forceHeavy: _retryCount > 1);
                if (!parsed.Success)
                {
                    _logger.Warn("AgentLoop", $"JSON repair failed on attempt {_retryCount}", request.AgentId);
                    if (_retryCount >= MaxRetries)
                        return new AgentResult(false, null, null, $"Failed to parse LLM output after {MaxRetries} attempts");
                    continue;
                }

                // 4. Check confidence
                var score = _scorer.Score(llmResponse, parsed.Json ?? "", null);
                _logger.Debug("AgentLoop", $"Confidence score: {score.Score:F2}", request.AgentId, data: new { signals = score.Signals });

                if (score.Score < 0.40)
                {
                    _logger.Warn("AgentLoop", $"Low confidence ({score.Score:F2}), retrying", request.AgentId);
                    if (_retryCount >= MaxRetries)
                        return new AgentResult(false, null, null, $"Low confidence after {MaxRetries} attempts");
                    continue;
                }

                // 5. Store to memory
                await _memory.StoreAsync(new MemoryEntry(
                    Guid.NewGuid().ToString(),
                    $"User: {request.Input}\nAgent: {llmResponse}",
                    request.UserId, "agent_loop",
                    DateTime.UtcNow
                ));

                return new AgentResult(true, parsed.Json, null, null);
            }

            return new AgentResult(false, null, null, $"Exhausted {MaxRetries} retries");
        }
        catch (OperationCanceledException)
        {
            return new AgentResult(false, null, null, "Request cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error("AgentLoop", $"Unhandled error: {ex.Message}", request.AgentId, data: new { ex.StackTrace });
            return new AgentResult(false, null, null, $"Internal error: {ex.Message}");
        }
    }
}

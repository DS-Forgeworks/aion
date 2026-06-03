using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const int MaxSteps = 20;

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
            // Build initial system prompt
            var recentMem = await _memory.GetRecentAsync(5);
            var recentStr = string.Join("\n", recentMem.Select(m => $"[{m.CreatedAt:HH:mm}] {m.Content[..Math.Min(m.Content.Length, 200)]}"));
            var tools = _toolRegistry.GetDefinitions();
            var toolStr = string.Join("\n", tools.Select(t => $"  - {t.Name}: {t.Description}"));

            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                _agentName, "", toolStr, _capabilityLevel,
                recentStr, "");

            // Context accumulates results between steps
            var context = new Dictionary<string, string>();
            var userInput = request.Input;
            var stepCount = 0;
            var previousSteps = new List<string>(); // track plan signatures

            while (stepCount < MaxSteps)
            {
                ct.ThrowIfCancellationRequested();
                stepCount++;

                // 1. Build the prompt with current context
                var contextStr = context.Count > 0
                    ? string.Join("\n", context.Select(kv => $"{kv.Key}: {kv.Value}"))
                    : "";

                var stepPrompt = BuildStepPrompt(systemPrompt, userInput, contextStr, request.Input);

                // 2. Call LLM
                var llmRequest = new LLMRequest(stepPrompt, "", Model: request.Model);
                string llmResponse;
                try
                {
                    llmResponse = await CallLlmWithRetry(llmRequest, ct);
                }
                catch (Exception ex)
                {
                    _logger.Warn("AgentLoop", $"LLM call failed after retries: {ex.Message}", request.AgentId);
                    return new AgentResult(false, null, null, "LLM call failed after retries");
                }

                if (string.IsNullOrWhiteSpace(llmResponse))
                    continue;

                // 3. Parse the plan
                var plan = ParsePlan(llmResponse, request);
                if (plan == null || plan.Count == 0)
                {
                    _logger.Debug("AgentLoop", "No plan steps found, continuing", request.AgentId);
                    continue;
                }

                // Dedup: skip if we've seen this exact plan
                var planSig = string.Join("|", plan.Select(s => $"{s.tool}:{s.inputStr?[..Math.Min(s.inputStr.Length, 50)]}"));
                if (previousSteps.Contains(planSig))
                {
                    _logger.Debug("AgentLoop", "Duplicate plan detected, breaking loop", request.AgentId);
                    if (plan.Count == 1 && plan[0].tool == "none")
                        return new AgentResult(true, plan[0].inputStr ?? "", null, null);
                    break;
                }
                previousSteps.Add(planSig);

                // 4. Execute each step in the plan
                string? lastResult = null;
                foreach (var step in plan)
                {
                    ct.ThrowIfCancellationRequested();

                    if (step.tool == "none")
                    {
                        // Terminal answer
                        lastResult = step.inputStr ?? "";
                        _logger.Info("AgentLoop", $"Final answer: {lastResult?[..Math.Min(lastResult.Length, 200)]}", request.AgentId);

                        await _memory.StoreAsync(new MemoryEntry(
                            Guid.NewGuid().ToString(),
                            $"User: {request.Input}\nAgent: {lastResult}",
                            request.UserId, "agent_loop",
                            DateTime.UtcNow
                        ));

                        // Substitute any remaining context placeholders
                        lastResult = ResolvePlaceholders(lastResult, context);
                        return new AgentResult(true, lastResult, null, null);
                    }

                    // Execute the tool
                    var resolvedInput = ResolvePlaceholders(step.inputStr, context);
                    _logger.Info("AgentLoop", $"Executing step {stepCount}.{plan.IndexOf(step) + 1}: {step.tool} with {resolvedInput?[..Math.Min(resolvedInput?.Length ?? 0, 100)]}",
                        request.AgentId);

                    var toolResult = await _toolRegistry.ExecuteAsync(step.tool, resolvedInput ?? "");
                    if (toolResult == null)
                    {
                        var err = $"Unknown tool: {step.tool}";
                        _logger.Warn("AgentLoop", err, request.AgentId);
                        context[$"{step.tool}_error"] = err;
                        lastResult = err;
                        break;
                    }

                    if (!toolResult.Success)
                    {
                        var err = $"Tool '{step.tool}' failed: {toolResult.Error}";
                        _logger.Warn("AgentLoop", err, request.AgentId);
                        context[$"{step.tool}_error"] = err;
                        lastResult = err;

                        // On tool failure, re-ask LLM
                        userInput = $"The tool '{step.tool}' failed with: {toolResult.Error}. The original request was: {request.Input}. What should I do instead?";
                        continue;
                    }

                    var output = toolResult.Output ?? "";
                    _logger.Info("AgentLoop", $"Tool '{step.tool}' returned: {output[..Math.Min(output.Length, 200)]}", request.AgentId);

                    // Store in context for subsequent steps
                    context[step.tool] = output;
                    context["last_result"] = output;
                    context["answer"] = output;
                    lastResult = output;
                }

                // If we got a lastResult but no 'none' step, feed it back for more planning
                if (lastResult != null)
                {
                    userInput = $"Previous result: {lastResult[..Math.Min(lastResult.Length, 500)]}\nContinue the original task: {request.Input}";
                }
            }

            _logger.Warn("AgentLoop", $"Reached max steps ({MaxSteps}), returning last result", request.AgentId);
            return new AgentResult(true, context.GetValueOrDefault("last_result") ?? "Task incomplete after maximum steps.", null, null);
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

    private async Task<string> CallLlmWithRetry(LLMRequest request, CancellationToken ct)
    {
        _retryCount = 0;
        while (_retryCount < MaxRetries)
        {
            _retryCount++;
            _logger.Debug("AgentLoop", $"LLM call attempt {_retryCount}/{MaxRetries}");

            try
            {
                var response = await _llm.GenerateAsync(request, ct);
                if (!string.IsNullOrWhiteSpace(response))
                    return response;
            }
            catch (Exception ex)
            {
                _logger.Warn("AgentLoop", $"LLM call failed: {ex.Message}", data: new { attempt = _retryCount });
                if (_retryCount >= MaxRetries) throw;
            }
        }
        throw new Exception("LLM returned empty response after retries");
    }

    private string BuildStepPrompt(string systemPrompt, string userInput, string context, string originalRequest)
    {
        var sb = new StringBuilder();
        sb.AppendLine(systemPrompt);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine("--- CONTEXT (from previous steps) ---");
            sb.AppendLine(context);
            sb.AppendLine("--- END CONTEXT ---");
            sb.AppendLine();
        }

        sb.AppendLine("--- REQUEST ---");
        sb.AppendLine(userInput);
        sb.AppendLine("--- END REQUEST ---");
        sb.AppendLine();
        sb.AppendLine("Respond with a JSON array of steps to execute. Each step has \"tool\" and \"input\".");
        sb.AppendLine("If the task is complete, respond with: [{\"tool\":\"none\",\"input\":{\"answer\":\"<your response>\"}}]");
        sb.AppendLine("Use context values like {search_results}, {last_result}, etc. in inputs.");

        return sb.ToString();
    }

    private List<(string tool, string? inputStr)>? ParsePlan(string llmResponse, AgentRequest request)
    {
        try
        {
            // Parse and repair
            var parsed = _repairer.Repair(llmResponse);
            if (!parsed.Success || string.IsNullOrWhiteSpace(parsed.Json))
            {
                _logger.Warn("AgentLoop", "JSON repair failed in plan parsing", request.AgentId);
                return null;
            }

            var score = _scorer.Score(llmResponse, parsed.Json, null);
            if (score.Score < 0.35)
            {
                _logger.Warn("AgentLoop", $"Low confidence ({score.Score:F2}) in plan", request.AgentId);
                return null;
            }

            using var doc = JsonDocument.Parse(parsed.Json);
            var root = doc.RootElement;

            JsonElement.ArrayEnumerator calls;
            if (root.ValueKind == JsonValueKind.Array)
                calls = root.EnumerateArray();
            else
                return null;

            var steps = new List<(string tool, string? inputStr)>();
            foreach (var call in calls)
            {
                if (!call.TryGetProperty("tool", out var toolEl)) continue;
                var tool = toolEl.GetString();
                if (string.IsNullOrWhiteSpace(tool)) continue;

                string? inputStr = null;
                if (call.TryGetProperty("input", out var inputEl))
                {
                    if (inputEl.ValueKind == JsonValueKind.String)
                        inputStr = inputEl.GetString();
                    else if (inputEl.ValueKind == JsonValueKind.Object)
                    {
                        // Try answer field first, then serialize the whole object
                        if (inputEl.TryGetProperty("answer", out var answerEl))
                            inputStr = answerEl.GetString() ?? inputEl.GetRawText();
                        else
                            inputStr = inputEl.GetRawText();
                    }
                    else
                        inputStr = inputEl.GetRawText();
                }

                steps.Add((tool, inputStr));
            }

            return steps;
        }
        catch (JsonException ex)
        {
            _logger.Warn("AgentLoop", $"Plan parse error: {ex.Message}", request.AgentId);
            return null;
        }
    }

    private string ResolvePlaceholders(string? input, Dictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? "";
        return Regex.Replace(input, @"\{(\w+)\}", match =>
        {
            var key = match.Groups[1].Value;
            return context.TryGetValue(key, out var val) ? val : match.Value;
        });
    }
}

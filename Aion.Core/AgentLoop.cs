using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.Core.Interfaces;
using Aion.Core.Planning;
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
    private readonly PlanExtractor _planExtractor;
    private readonly IConfidenceScorer _scorer;
    private readonly ISafetyGate _safety;
    private readonly IMemoryStore _memory;
    private readonly IPlanStore _planStore;
    private readonly IContentSanitizer _sanitizer;
    private readonly IAionLogger _logger;
    private readonly string _agentName;
    private readonly int _capabilityLevel;

    private const int MaxSteps = 10;
    private const int MaxPlanRetries = 3;

    public AgentLoop(
        LlmService llm,
        PromptBuilder promptBuilder,
        ToolRegistry toolRegistry,
        PlanExtractor planExtractor,
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
        _planExtractor = planExtractor;
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
        _logger.Info("AgentLoop", $"Running request: {Truncate(request.Input, 100)}", request.AgentId);

        try
        {
            // Build system prompt once
            var recentMem = await _memory.GetRecentAsync(5);
            var recentStr = string.Join("\n", recentMem.Select(m =>
                $"[{m.CreatedAt:HH:mm}] {Truncate(m.Content, 200)}"));
            var tools = _toolRegistry.GetDefinitions();
            var toolStr = string.Join("\n", tools.Select(t => $"  - {t.Name}: {t.Description}"));

            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                _agentName, "", toolStr, _capabilityLevel, recentStr, "");

            // Context accumulates results between steps
            var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var userInput = request.Input;
            var stepCount = 0;
            var previousStepKeys = new HashSet<string>();

            while (stepCount < MaxSteps)
            {
                ct.ThrowIfCancellationRequested();
                stepCount++;

                // 1. Build prompt for this iteration
                var contextStr = context.Count > 0
                    ? string.Join("\n", context.Select(kv => $"{{{kv.Key}}}: {Truncate(kv.Value, 300)}"))
                    : "";

                var prompt = BuildStepPrompt(systemPrompt, userInput, contextStr, request.Input);

                // 2. Call LLM
                var llmRequest = new LLMRequest(prompt, "", Model: request.Model);
                var llmResponse = await CallLlmWithRetryAsync(llmRequest, ct);
                if (string.IsNullOrWhiteSpace(llmResponse))
                {
                    _logger.Warn("AgentLoop", "Empty LLM response, aborting", request.AgentId);
                    return new AgentResult(false, null, null, "LLM returned empty response");
                }

                // 3. Parse the plan (PlanExtractor handles retry itself)
                var plan = _planExtractor.ExtractPlan(llmResponse, async (retryPrompt) =>
                {
                    var retryReq = new LLMRequest(retryPrompt, "", Model: request.Model);
                    return await _llm.GenerateAsync(retryReq, ct);
                });

                // Score the plan, not the raw response
                var planJson = JsonSerializer.Serialize(plan.Select(s => new { tool = s.Tool, input = s.Input }));
                var score = _scorer.Score(llmResponse, planJson, null);
                if (score.Score < 0.20)
                {
                    _logger.Warn("AgentLoop", $"Very low confidence ({score.Score:F2}), retrying LLM", request.AgentId);
                    userInput = $"The previous response had low confidence. The original task was: {request.Input}. Please respond with a valid JSON array.";
                    continue;
                }

                if (plan.Count == 0)
                {
                    _logger.Warn("AgentLoop", "Plan extraction returned empty, retrying", request.AgentId);
                    continue;
                }

                // Fallback from PlanExtractor (error step) — deliver as-is
                if (plan.Count == 1 && plan[0].Tool == "none" && plan[0].Input != null && plan[0].Input.Contains("Error"))
                {
                    _logger.Warn("AgentLoop", $"Plan extractor fallback: {Truncate(plan[0].Input, 200)}", request.AgentId);
                    var errMsg = ResolvePlaceholders(plan[0].Input, context);
                    return new AgentResult(false, null, null, errMsg);
                }

                // Dedup: same plan pattern as before = loop detected
                var planKey = string.Join("|", plan.Select(s => $"{s.Tool}:{Truncate(s.Input, 50)}"));
                if (previousStepKeys.Contains(planKey))
                {
                    _logger.Debug("AgentLoop", "Duplicate plan detected, delivering last result", request.AgentId);
                    var lastAnswer = context.GetValueOrDefault("answer") ?? context.GetValueOrDefault("last_result") ?? "Task complete.";
                    return new AgentResult(true, lastAnswer, null, null);
                }
                previousStepKeys.Add(planKey);

                // 4. Execute each step
                string? lastResult = null;
                foreach (var step in plan)
                {
                    ct.ThrowIfCancellationRequested();

                    if (step.Tool == "none")
                    {
                        var answer = ResolvePlaceholders(step.Input, context) ?? "";
                        _logger.Info("AgentLoop", $"Final answer: {Truncate(answer, 200)}", request.AgentId);

                        await StoreMemory(request, answer);
                        return new AgentResult(true, answer, null, null);
                    }

                    // Execute tool
                    var resolvedInput = ResolvePlaceholders(step.Input, context);
                    _logger.Info("AgentLoop",
                        $"Step {stepCount}: {step.Tool} ({Truncate(resolvedInput, 100)})", request.AgentId);

                    var toolResult = await _toolRegistry.ExecuteAsync(step.Tool, resolvedInput ?? "");
                    if (toolResult == null)
                    {
                        var err = $"Unknown tool: {step.Tool}";
                        _logger.Warn("AgentLoop", err, request.AgentId);
                        context["error"] = err;
                        lastResult = err;
                        break;
                    }

                    if (!toolResult.Success)
                    {
                        var err = $"Tool '{step.Tool}' failed: {toolResult.Error}";
                        _logger.Warn("AgentLoop", err, request.AgentId);
                        context["error"] = err;
                        lastResult = err;

                        // Feed error back to LLM for re-planning
                        userInput = $"The tool '{step.Tool}' failed: {toolResult.Error}\nOriginal request: {request.Input}\nWhat should I do?";
                        continue;
                    }

                    var output = toolResult.Output ?? "";
                    _logger.Info("AgentLoop", $"{step.Tool} → {Truncate(output, 200)}", request.AgentId);

                    // Store in context (both tool-specific and generic keys)
                    context[step.Tool] = output;
                    context["last_result"] = output;

                    // Also store as generic keys the planner might reference
                    if (step.Tool == "web_search" || step.Tool == "search") context["search_results"] = output;
                    if (step.Tool == "read_file" || step.Tool == "read") context["file_content"] = output;
                    if (step.Tool == "calculator") context["calculation"] = output;
                    if (step.Tool == "now") context["time"] = output;

                    lastResult = output;
                }

                // If we executed steps but the last step wasn't "none", feed back for more
                if (lastResult != null && plan.All(s => s.Tool != "none"))
                {
                    userInput = $"Result: {Truncate(lastResult, 500)}\nContinue: {request.Input}";
                }
            }

            _logger.Warn("AgentLoop", $"Max steps ({MaxSteps}) reached", request.AgentId);
            var final = context.GetValueOrDefault("answer") ?? context.GetValueOrDefault("last_result") ?? "Task incomplete.";
            return new AgentResult(true, final, null, null);
        }
        catch (OperationCanceledException)
        {
            return new AgentResult(false, null, null, "Request cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error("AgentLoop", $"Error: {ex.Message}", request.AgentId, data: new { ex.StackTrace });
            return new AgentResult(false, null, null, $"Internal error: {ex.Message}");
        }
    }

    private async Task<string> CallLlmWithRetryAsync(LLMRequest request, CancellationToken ct)
    {
        for (int i = 1; i <= 3; i++)
        {
            _logger.Debug("AgentLoop", $"LLM attempt {i}/3");
            try
            {
                var response = await _llm.GenerateAsync(request, ct);
                if (!string.IsNullOrWhiteSpace(response))
                    return response;
            }
            catch (Exception ex)
            {
                _logger.Warn("AgentLoop", $"LLM attempt {i} failed: {ex.Message}");
                if (i >= 3) throw;
            }
        }
        throw new Exception("LLM returned empty after 3 attempts");
    }

    private static string BuildStepPrompt(string systemPrompt, string userInput, string context, string originalRequest)
    {
        var sb = new StringBuilder();
        sb.AppendLine(systemPrompt);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine("--- CONTEXT (available for {{placeholder}} substitution) ---");
            sb.AppendLine(context);
            sb.AppendLine("--- ---");
            sb.AppendLine();
        }

        sb.AppendLine("--- REQUEST ---");
        sb.AppendLine(userInput);
        sb.AppendLine("--- ---");
        sb.AppendLine();
        sb.AppendLine("Respond with a JSON array of tool steps. Each step has \"tool\" and \"input\".");
        sb.AppendLine("Example: [{\"tool\":\"calculator\",\"input\":{\"expression\":\"2+2\"}}]");
        sb.AppendLine("When done: [{\"tool\":\"none\",\"input\":{\"answer\":\"Your final response here\"}}]");
        sb.AppendLine("You can reference previous results with {tool_name} placeholders.");

        return sb.ToString();
    }

    private static string ResolvePlaceholders(string? input, Dictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? "";
        return Regex.Replace(input, @"\{(\w+)\}", match =>
            context.TryGetValue(match.Groups[1].Value, out var val) ? val : match.Value);
    }

    private async Task StoreMemory(AgentRequest request, string answer)
    {
        await _memory.StoreAsync(new MemoryEntry(
            Guid.NewGuid().ToString(),
            $"User: {request.Input}\nAgent: {answer}",
            request.UserId, "agent_loop",
            DateTime.UtcNow));
    }

    private static string Truncate(string? s, int max) =>
        s != null && s.Length > max ? s[..max] + "..." : (s ?? "");
}

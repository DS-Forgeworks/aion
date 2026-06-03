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

    private const int MaxSteps = 15;
    private const int MaxPlanRetries = 3;

    // Plan file stored at: ~/.aion/plans/{sessionId}.plan.json
    private static readonly string PlansDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aion", "plans");

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
            // Build the system prompt once (tools, memory, identity)
            var systemPrompt = await BuildSystemPromptAsync();

            // Phase 1: Generate or resume the plan
            var planFile = Path.Combine(PlansDir, $"{request.SessionId}.plan.json");
            var planSteps = await LoadOrCreatePlanAsync(planFile, systemPrompt, request, ct);

            // Phase 2: Execute the plan step by step
            var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lastToolOutput = "";
            var stepIndex = 0;

            while (stepIndex < planSteps.Count && stepIndex < MaxSteps)
            {
                ct.ThrowIfCancellationRequested();
                var step = planSteps[stepIndex];

                // Skip already-completed steps
                if (step.Status == "completed")
                {
                    if (step.Result != null)
                        context[step.Tool] = step.Result;
                    stepIndex++;
                    continue;
                }

                // Mark step as in_progress
                step.Status = "in_progress";
                await SavePlanFileAsync(planFile, planSteps);

                // If this is a "none" step (final answer), deliver it
                if (step.Tool == "none")
                {
                    var answer = ResolvePlaceholders(step.Input, context) ?? "Task complete.";
                    step.Status = "completed";
                    step.Result = answer;
                    await SavePlanFileAsync(planFile, planSteps);

                    _logger.Info("AgentLoop", $"Final answer: {Truncate(answer, 200)}", request.AgentId);
                    await StoreMemory(request, answer);
                    return new AgentResult(true, answer, null, null);
                }

                // Execute the tool
                var resolvedInput = ResolvePlaceholders(step.Input, context);
                _logger.Info("AgentLoop",
                    $"Step {stepIndex + 1}/{planSteps.Count}: {step.Tool} ({Truncate(resolvedInput, 100)})", request.AgentId);

                if (ct.IsCancellationRequested) break;
                var toolResult = await _toolRegistry.ExecuteAsync(step.Tool, resolvedInput ?? "");

                if (toolResult == null)
                {
                    var err = $"Unknown tool: {step.Tool}";
                    step.Status = "failed";
                    step.Error = err;
                    await SavePlanFileAsync(planFile, planSteps);
                    _logger.Warn("AgentLoop", err, request.AgentId);

                    // Let the LLM re-plan this failed step
                    var fixPlan = await ReplanStepAsync(systemPrompt, request, step, err, context, ct);
                    if (fixPlan != null)
                    {
                        planSteps = MergePlan(planSteps, stepIndex, fixPlan);
                        await SavePlanFileAsync(planFile, planSteps);
                        // Re-execute from the merged-in replacement steps
                        continue;
                    }
                    stepIndex++;
                    continue;
                }

                if (!toolResult.Success)
                {
                    var err = $"Tool '{step.Tool}' failed: {toolResult.Error}";
                    step.Status = "failed";
                    step.Error = err;
                    await SavePlanFileAsync(planFile, planSteps);
                    _logger.Warn("AgentLoop", err, request.AgentId);

                    // Let the LLM re-plan
                    var fixPlan = await ReplanStepAsync(systemPrompt, request, step, err, context, ct);
                    if (fixPlan != null)
                    {
                        planSteps = MergePlan(planSteps, stepIndex, fixPlan);
                        await SavePlanFileAsync(planFile, planSteps);
                        continue;
                    }
                    stepIndex++;
                    continue;
                }

                // Success
                lastToolOutput = toolResult.Output ?? "";
                step.Status = "completed";
                step.Result = lastToolOutput;
                context[step.Tool] = lastToolOutput;
                context["last_result"] = lastToolOutput;

                // Store semantic context keys for placeholder resolution
                if (step.Tool is "web_search" or "search") context["search_results"] = lastToolOutput;
                if (step.Tool is "read_file" or "read") context["file_content"] = lastToolOutput;
                if (step.Tool == "calculator") context["calculation"] = lastToolOutput;
                if (step.Tool == "now") context["time"] = lastToolOutput;
                if (step.Tool == "web_fetch") context["page_content"] = lastToolOutput;

                await SavePlanFileAsync(planFile, planSteps);

                // Log progress
                var done = planSteps.Count(s => s.Status == "completed" || s.Status == "failed");
                _logger.Info("AgentLoop",
                    $"Step {stepIndex + 1}/{planSteps.Count} complete ({done}/{planSteps.Count})", request.AgentId);

                stepIndex++;
            }

            // All steps done or max reached — finalize
            if (planSteps.All(s => s.Status == "completed"))
            {
                var lastStep = planSteps.LastOrDefault(s => s.Tool == "none");
                if (lastStep != null && lastStep.Result != null)
                {
                    await StoreMemory(request, lastStep.Result);
                    return new AgentResult(true, lastStep.Result, null, null);
                }

                // No "none" step — summarize with LLM
                var summary = await BuildSummaryAsync(systemPrompt, request, context, planSteps, ct);
                await StoreMemory(request, summary);
                return new AgentResult(true, summary, null, null);
            }

            // Max steps reached
            _logger.Warn("AgentLoop", $"Max steps ({MaxSteps}) reached", request.AgentId);
            planSteps.ForEach(s => { if (s.Status == "in_progress") s.Status = "paused"; });
            await SavePlanFileAsync(planFile, planSteps);

            var partial = context.GetValueOrDefault("answer") ?? context.GetValueOrDefault("last_result") ?? "Plan paused — more steps remaining.";
            return new AgentResult(true, partial, null, null);
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

    /// <summary>
    /// Phase 1: Load existing plan file OR ask the LLM to create one.
    /// The plan is a flat JSON array of steps saved to disk.
    /// </summary>
    private async Task<List<StepEntry>> LoadOrCreatePlanAsync(
        string planFile, string systemPrompt, AgentRequest request, CancellationToken ct)
    {
        // Try loading existing plan (for resume)
        if (File.Exists(planFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(planFile, ct);
                var steps = JsonSerializer.Deserialize<List<StepEntry>>(json);
                if (steps != null && steps.Count > 0)
                {
                    var pending = steps.Any(s => s.Status is "pending" or "in_progress" or "failed");
                    if (pending)
                    {
                        _logger.Info("AgentLoop", $"Resuming plan from {planFile} ({steps.Count} steps, {steps.Count(s => s.Status == "completed")} done)");
                        return steps;
                    }
                }
            }
            catch { /* Corrupted file — regenerate */ }
        }

        // Generate a new plan via LLM
        _logger.Info("AgentLoop", "Generating plan...", request.AgentId);

        Directory.CreateDirectory(PlansDir);

        var planPrompt = BuildPlanPrompt(systemPrompt, request.Input);
        var planLlmRequest = new LLMRequest(planPrompt, "", Model: request.Model);
        var planResponse = await CallLlmWithRetryAsync(planLlmRequest, ct);

        // Parse the plan from LLM response
        var planSteps = _planExtractor.ExtractPlan(planResponse, async (retryPrompt) =>
        {
            var retryReq = new LLMRequest(retryPrompt, "", Model: request.Model);
            return await _llm.GenerateAsync(retryReq, ct);
        });

        // Convert PlanSteps to StepEntries
        var entries = new List<StepEntry>();
        for (int i = 0; i < planSteps.Count; i++)
        {
            entries.Add(new StepEntry
            {
                Step = i,
                Tool = planSteps[i].Tool,
                Input = planSteps[i].Input,
                Status = "pending"
            });
        }

        if (entries.Count == 0)
        {
            entries.Add(new StepEntry
            {
                Step = 0,
                Tool = "none",
                Input = "Could not create a plan for that request.",
                Status = "completed",
                Result = "Could not create a plan for that request."
            });
        }

        await SavePlanFileAsync(planFile, entries);
        _logger.Info("AgentLoop", $"Plan created: {entries.Count} steps", request.AgentId);

        return entries;
    }

    /// <summary>
    /// Build a prompt that asks the LLM to create a plan as a JSON array of steps.
    /// Each step has tool + input + a brief note about what it does.
    /// </summary>
    private string BuildPlanPrompt(string systemPrompt, string userInput)
    {
        var sb = new StringBuilder();
        sb.AppendLine(systemPrompt);
        sb.AppendLine();
        sb.AppendLine("## PLAN CREATION INSTRUCTIONS");
        sb.AppendLine("You are creating a step-by-step plan to fulfill the user's request.");
        sb.AppendLine("Each step should use exactly one tool. Steps run in order.");
        sb.AppendLine("");
        sb.AppendLine("Available tools and what they do:");
        sb.AppendLine("  - calculator: Evaluate a mathematical expression");
        sb.AppendLine("  - now: Get the current date and time");
        sb.AppendLine("  - web_search: Search the web (DuckDuckGo)");
        sb.AppendLine("  - web_fetch: Fetch content from a URL");
        sb.AppendLine("  - read_file: Read contents of a file from the filesystem");
        sb.AppendLine("  - write_file: Write content to a file on the filesystem");
        sb.AppendLine("  - shell_command: Execute a shell command (Linux/Mac)");
        sb.AppendLine("  - sandbox: Execute code in a sandboxed environment (Python/Node)");
        sb.AppendLine("  - remember: Store a fact in long-term memory");
        sb.AppendLine("  - recall: Retrieve facts from long-term memory");
        sb.AppendLine("  - schedule: Schedule a reminder or task");
        sb.AppendLine("  - none: Deliver the final answer to the user (last step only)");
        sb.AppendLine();
        sb.AppendLine("Response format — output ONLY a JSON array. No other text.");
        sb.AppendLine("[");
        sb.AppendLine("  {\"tool\":\"tool_name\",\"input\":{\"key\":\"value\"},\"note\":\"brief description of what this step does\"},");
        sb.AppendLine("  {\"tool\":\"none\",\"input\":{\"answer\":\"Final response for the user\"}}");
        sb.AppendLine("]");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT:");
        sb.AppendLine("- Each step must be a single tool call. Don't combine tools in one step.");
        sb.AppendLine("- The last step should use tool \"none\" with the final answer.");
        sb.AppendLine("- Results from earlier steps can be referenced as {tool_name} in later steps.");
        sb.AppendLine("- If the request is simple (greeting, chit-chat), use just: [{\"tool\":\"none\",\"input\":{\"answer\":\"Your reply\"}}]");
        sb.AppendLine("- Break complex requests into logical steps. Example:");
        sb.AppendLine("  User: \"What time is it and what's 255/5?\"");
        sb.AppendLine("  Plan: [");
        sb.AppendLine("    {\"tool\":\"now\",\"input\":{},\"note\":\"get current time\"},");
        sb.AppendLine("    {\"tool\":\"calculator\",\"input\":{\"expression\":\"255/5\"},\"note\":\"calculate 255 divided by 5\"},");
        sb.AppendLine("    {\"tool\":\"none\",\"input\":{\"answer\":\"The time is {now} and 255 divided by 5 equals {calculator}.\"}}");
        sb.AppendLine("  ]");
        sb.AppendLine();
        sb.AppendLine($"## USER REQUEST:");
        sb.AppendLine(userInput);
        sb.AppendLine();
        sb.AppendLine("Return ONLY the JSON array plan. Begin with [ and end with ].");
        sb.AppendLine("Use \\n for newlines in strings.");

        return sb.ToString();
    }

    /// <summary>
    /// When a tool step fails, ask the LLM for replacement steps.
    /// </summary>
    private async Task<List<Aion.Core.Planning.PlanStep>?> ReplanStepAsync(
        string systemPrompt, AgentRequest request, StepEntry failedStep,
        string error, Dictionary<string, string> context, CancellationToken ct)
    {
        var contextStr = context.Count > 0
            ? string.Join("\n", context.Select(kv => $"  {kv.Key}: {Truncate(kv.Value, 200)}"))
            : "(none)";

        var prompt = $@"{systemPrompt}

## RE-PLAN REQUIRED
A step in the plan failed. Generate 1-2 replacement steps to handle this.

Failed step: tool={failedStep.Tool}, input={failedStep.Input}
Error: {error}

Context so far:
{contextStr}

Respond with a JSON array of the replacement step(s).
Example: [{{""tool"":""calculator"",""input"":{{""expression"":""2+2""}}}}]";

        var llmRequest = new LLMRequest(prompt, "", Model: request.Model);
        try
        {
            var response = await CallLlmWithRetryAsync(llmRequest, ct);
            return _planExtractor.ExtractPlan(response);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Build a summary when the plan completes without a "none" step.
    /// </summary>
    private async Task<string> BuildSummaryAsync(
        string systemPrompt, AgentRequest request,
        Dictionary<string, string> context, List<StepEntry> steps, CancellationToken ct)
    {
        var stepsStr = string.Join("\n", steps.Select(s =>
            $"  Step {s.Step + 1}: {s.Tool} → {Truncate(s.Result, 100)}"));

        var contextStr = string.Join("\n", context.Select(kv => $"  {kv.Key}: {Truncate(kv.Value, 200)}"));

        var prompt = $@"{systemPrompt}

## SUMMARIZE RESULTS
The plan is complete. Here is what happened:

Steps executed:
{stepsStr}

Results:
{contextStr}

Original request: {request.Input}

Provide a natural-language summary for the user. Include all relevant results.";

        try
        {
            var req = new LLMRequest(prompt, "", Model: request.Model);
            return await CallLlmWithRetryAsync(req, ct);
        }
        catch
        {
            return context.GetValueOrDefault("last_result") ?? "Plan complete.";
        }
    }

    /// <summary>
    /// Merge new steps into the plan at the given index.
    /// </summary>
    private static List<StepEntry> MergePlan(List<StepEntry> existing, int atIndex, List<Aion.Core.Planning.PlanStep> newSteps)
    {
        var result = existing.Take(atIndex).ToList();

        // Mark the failed step as failed
        if (atIndex < existing.Count)
        {
            existing[atIndex].Status = "failed";
            result.Add(existing[atIndex]);
        }

        // Add new steps
        int offset = result.Count;
        foreach (var ns in newSteps)
        {
            result.Add(new StepEntry
            {
                Step = offset++,
                Tool = ns.Tool,
                Input = ns.Input,
                Status = "pending",
            });
        }

        // Add remaining existing steps, re-indexed
        for (int i = atIndex + 1; i < existing.Count; i++)
        {
            existing[i].Step = offset++;
            result.Add(existing[i]);
        }

        return result;
    }

    private async Task<string> BuildSystemPromptAsync()
    {
        var recentMem = await _memory.GetRecentAsync(5);
        var recentStr = string.Join("\n", recentMem.Select(m =>
            $"[{m.CreatedAt:HH:mm}] {Truncate(m.Content, 200)}"));
        var tools = _toolRegistry.GetDefinitions();
        var toolStr = string.Join("\n", tools.Select(t => $"  - {t.Name}: {t.Description}"));

        return _promptBuilder.BuildSystemPrompt(
            _agentName, "", toolStr, _capabilityLevel, recentStr, "");
    }

    private async Task SavePlanFileAsync(string path, List<StepEntry> steps)
    {
        try
        {
            var json = JsonSerializer.Serialize(steps, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            _logger.Warn("AgentLoop", $"Failed to save plan file: {ex.Message}");
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
            $"User: {Truncate(request.Input, 500)}\nAgent: {Truncate(answer, 2000)}",
            request.UserId, "agent_loop",
            DateTime.UtcNow));
    }

    private static string Truncate(string? s, int max) =>
        s != null && s.Length > max ? s[..max] + "..." : (s ?? "");
}

/// <summary>
/// Serializable plan step entry stored in the plan file.
/// </summary>
public class StepEntry
{
    public int Step { get; set; }
    public string Tool { get; set; } = "";
    public string? Input { get; set; }
    public string? Result { get; set; }
    public string Status { get; set; } = "pending"; // pending | in_progress | completed | failed | paused
    public string? Error { get; set; }
}

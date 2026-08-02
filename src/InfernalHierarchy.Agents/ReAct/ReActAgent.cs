using BaseAgentImpl = InfernalHierarchy.Agents.Base.BaseAgent;
using System.Text;
using System.Text.Json;

namespace InfernalHierarchy.Agents.ReAct;

/// <summary>
/// ReAct (Reasoning + Acting) agent implementation
/// Implements the Thought → Action → Observation loop
/// </summary>
public class ReActAgent : BaseAgentImpl
{
    private readonly IVectorMemory? _vectorMemory;
    private readonly RagOptions _ragOptions;
    private readonly CritiqueOptions _critiqueOptions;
    private readonly IAgentFactory _agentFactory;
    private readonly IReActTaskProcessor _taskProcessor;
    private readonly ReActTaskProcessorContext _processorContext;
    private readonly IRagContextEnricher _ragContextEnricher;

    private sealed class CritiqueResult
    {
        public int QualityScore { get; set; }
        public List<string>? Contradictions { get; set; }
        public List<string>? MissingSources { get; set; }
        public List<string>? Recommendations { get; set; }
        public bool ShouldRollback { get; set; }
        public bool ShouldKillBranch { get; set; }
        public string? ImprovedSummary { get; set; }
    }

    public ReActAgent(
        Agent agent,
        Persona persona,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        IAgentFactory agentFactory,
        ILlmClient ollamaClient,
        ILogger<ReActAgent> logger)
        : this(
            agent,
            persona,
            messageBus,
            sharedMemory,
            toolRegistry,
            agentFactory,
            ollamaClient,
            logger,
            eventSink: null)
    {
    }

    public ReActAgent(
        Agent agent,
        Persona persona,
        IMessageBus messageBus,
        ISharedMemory sharedMemory,
        IToolRegistry toolRegistry,
        IAgentFactory agentFactory,
        ILlmClient ollamaClient,
        ILogger<ReActAgent> logger,
        IAgentEventSink? eventSink,
        IVectorMemory? vectorMemory = null,
        RagOptions? ragOptions = null,
        ReActOptions? reActOptions = null,
        CritiqueOptions? critiqueOptions = null,
        TokenUsageTracker? tokenUsageTracker = null,
        MultiModelLlmClient? multiModelLlmClient = null,
        IAgentCollaborationService? collaborationService = null,
        IActionParser? actionParser = null,
        IActionInputParser? actionInputParser = null,
        IActionExecutor? actionExecutor = null,
        IReportGenerator? reportGenerator = null,
        IReActPromptBuilder? promptBuilder = null,
        IReActLoopRunner? loopRunner = null,
        IReActTaskProcessor? taskProcessor = null,
        IRagContextEnricher? ragContextEnricher = null,
        IAgentEventAppender? agentEventAppender = null,
        IAgentSkillRuntimeStore? runtimeSkillStore = null)
        : base(agent, persona, messageBus, sharedMemory, toolRegistry, logger)
    {
        _vectorMemory = vectorMemory;
        _ragOptions = ragOptions ?? new RagOptions();
        _critiqueOptions = critiqueOptions ?? new CritiqueOptions();
        _agentFactory = agentFactory;
        var effectiveReActOptions = reActOptions ?? new ReActOptions();

        var effectiveActionParser = actionParser ?? new DefaultActionParser();
        var effectiveInputParser = actionInputParser ?? new DefaultActionInputParser(_logger);
        var effectiveActionExecutor = actionExecutor ?? new DefaultActionExecutor(effectiveInputParser);
        var effectiveReportGenerator = reportGenerator ?? new DefaultReportGenerator(tokenUsageTracker, multiModelLlmClient);

        var effectivePromptBuilder = promptBuilder ?? new DefaultReActPromptBuilder();
        var effectiveLoopRunner = loopRunner ?? new DefaultReActLoopRunner();

        _ragContextEnricher = ragContextEnricher ?? new DefaultRagContextEnricher();
        var effectiveEventAppender = agentEventAppender ?? new DefaultAgentEventAppender();

        _taskProcessor = taskProcessor ?? new DefaultReActTaskProcessor(_ragContextEnricher, effectiveEventAppender);

        _processorContext = new ReActTaskProcessorContext(
            AgentId: Id,
            AgentName: Name,
            AgentRank: Rank,
            Persona: Persona,
            LlmClient: ollamaClient,
            ToolRegistry: _toolRegistry,
            SharedMemory: _sharedMemory,
            ActionParser: effectiveActionParser,
            ActionExecutor: effectiveActionExecutor,
            ReportGenerator: effectiveReportGenerator,
            PromptBuilder: effectivePromptBuilder,
            LoopRunner: effectiveLoopRunner,
            ReActOptions: effectiveReActOptions,
            RagOptions: _ragOptions,
            VectorMemory: _vectorMemory,
            CollaborationService: collaborationService,
            RuntimeSkillStore: runtimeSkillStore,
            EventSink: eventSink,
            SetStatus: s => this.SetStatus(s, reason: "react"),
            BuildBaseContextAsync: (message, token) => base.BuildContextAsync(message, token),
            Logger: _logger);

    }

    protected override async Task<string> BuildContextAsync(AgentMessage task, CancellationToken ct)
    {
        var context = await base.BuildContextAsync(task, ct).ConfigureAwait(false);

        return await _ragContextEnricher.EnrichAsync(
            context,
            query: task.Content,
            agentId: Id,
            agentRank: Rank,
            vectorMemory: _vectorMemory,
            ragOptions: _ragOptions,
            logger: _logger,
            ct: ct).ConfigureAwait(false);
    }

    public override async Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default)
    {
        var response = await _taskProcessor.ProcessAsync(_processorContext, task, ct).ConfigureAwait(false);

        if (!ShouldRunCritique(task, response))
        {
            return response;
        }

        return await ApplyCritiqueAsync(task, response, ct).ConfigureAwait(false);
    }

    private bool ShouldRunCritique(AgentMessage task, AgentMessage response)
    {
        if (!_critiqueOptions.Enabled)
        {
            return false;
        }

        if (Rank is not (AgentRank.Supreme or AgentRank.Prince))
        {
            return false;
        }

        // Only critique completed reports (end-of-branch behavior).
        if (response.Type != MessageType.Report)
        {
            return false;
        }

        // Avoid critiquing supervisor replans by default; they are already a meta-intervention.
        if (task.Type == MessageType.Command && task.Content.StartsWith("SUPERVISOR_REPLAN:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var toolCallsCount = TryGetToolCallsCount(response.Payload);
        var depth = ComputeDepth();
        var explicitRequest = ContainsAnyKeyword(task.Content, _critiqueOptions.TriggerKeywords);

        return explicitRequest || depth >= _critiqueOptions.MinDepth || toolCallsCount >= _critiqueOptions.MinToolCalls;
    }

    private async Task<AgentMessage> ApplyCritiqueAsync(AgentMessage task, AgentMessage response, CancellationToken ct)
    {
        IAgent? critic = null;
        try
        {
            critic = await _agentFactory.CreateAgentAsync(
                _critiqueOptions.CriticPersonaName,
                _critiqueOptions.CriticRank,
                parentId: Id,
                ct: ct).ConfigureAwait(false);

            var toolCalls = TryGetToolCalls(response.Payload);
            var depth = ComputeDepth();
            var recentDecisions = await GetRecentDecisionsForThisAgentAsync(ct).ConfigureAwait(false);
            var critiquePrompt = BuildCritiquePrompt(task, response, toolCalls, depth, recentDecisions);

            var critiqueTask = new AgentMessage
            {
                FromAgentId = Id,
                ToAgentId = critic.Id,
                Type = MessageType.Task,
                Content = critiquePrompt,
                Payload = new Dictionary<string, object>
                {
                    ["critique_of_agent_id"] = Id,
                    ["critique_of_agent_rank"] = Rank.ToString(),
                    ["critique_depth"] = depth
                }
            };

            // IMPORTANT: Critique should be non-invasive.
            // Running a full ReAct loop for the critic can cause tool spam, stalls, and feedback loops.
            // Instead, we do a single completion using the critic persona's system prompt.
            var critiqueText = await _processorContext.LlmClient
                .GetCompletionAsync(critic.Persona.SystemPrompt, critiqueTask.Content, ct)
                .ConfigureAwait(false);

            var critiqueResponse = new AgentMessage
            {
                FromAgentId = critic.Id,
                ToAgentId = Id,
                Type = MessageType.Report,
                Content = critiqueText,
                Payload = new Dictionary<string, object>
                {
                    ["critic_persona"] = critic.Persona.Name,
                    ["critic_rank"] = critic.Rank.ToString()
                }
            };

            var mergedPayload = new Dictionary<string, object>(response.Payload ?? new Dictionary<string, object>())
            {
                ["critique_agent"] = critic.Name,
                ["critique"] = critiqueResponse.Content
            };

            if (TryParseCritiqueJson(critiqueResponse.Content, out var parsed) && parsed != null)
            {
                mergedPayload["critique_quality_score"] = parsed.QualityScore;
                mergedPayload["critique_should_rollback"] = parsed.ShouldRollback;
                mergedPayload["critique_should_kill_branch"] = parsed.ShouldKillBranch;

                if (!string.IsNullOrWhiteSpace(parsed.ImprovedSummary))
                {
                    response = new AgentMessage
                    {
                        FromAgentId = response.FromAgentId,
                        ToAgentId = response.ToAgentId,
                        Type = response.Type,
                        Content = parsed.ImprovedSummary!,
                        Payload = mergedPayload
                    };

                    mergedPayload["critique_applied"] = true;
                    return response;
                }
            }

            return new AgentMessage
            {
                FromAgentId = response.FromAgentId,
                ToAgentId = response.ToAgentId,
                Type = response.Type,
                Content = response.Content,
                Payload = mergedPayload
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _processorContext.Logger.LogWarning(ex, "Critique loop failed for agent {AgentName} ({AgentId})", Name, Id);
            return response;
        }
        finally
        {
            if (critic != null)
            {
                try
                {
                    await _agentFactory.TerminateAgentAsync(critic.Id, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _processorContext.Logger.LogDebug(ex, "Failed to terminate critic agent {CriticId}", critic.Id);
                }
            }
        }
    }

    private int ComputeDepth()
    {
        var depth = 1;
        var currentParentId = ParentAgentId;

        // Defensive cap: avoid cycles.
        for (var i = 0; i < 32 && !string.IsNullOrWhiteSpace(currentParentId); i++)
        {
            depth++;
            var parent = _agentFactory.GetAgent(currentParentId!);
            if (parent is BaseAgentImpl ba && !string.IsNullOrWhiteSpace(ba.ParentAgentId))
            {
                currentParentId = ba.ParentAgentId;
                continue;
            }

            break;
        }

        return depth;
    }

    private static int TryGetToolCallsCount(Dictionary<string, object>? payload)
        => TryGetToolCalls(payload).Count;

    private static List<string> TryGetToolCalls(Dictionary<string, object>? payload)
    {
        if (payload == null)
        {
            return new List<string>();
        }

        if (!payload.TryGetValue("tool_calls", out var value) || value == null)
        {
            return new List<string>();
        }

        if (value is IEnumerable<string> stringEnumerable)
        {
            return stringEnumerable.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        // Sometimes tool_calls might be serialized as object[] or JsonElement.
        if (value is IEnumerable<object> objEnumerable)
        {
            return objEnumerable.Select(o => o?.ToString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        if (value is JsonElement el && el.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        list.Add(s);
                    }
                }
            }

            return list;
        }

        return new List<string>();
    }

    private async Task<List<Decision>> GetRecentDecisionsForThisAgentAsync(CancellationToken ct)
    {
        var recent = await _sharedMemory.GetRecentDecisionsAsync(50, ct).ConfigureAwait(false);
        return recent
            .Where(d => string.Equals(d.CreatedBy, Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.CreatedAt)
            .Take(10)
            .ToList();
    }

    private static string BuildCritiquePrompt(
        AgentMessage originalTask,
        AgentMessage branchResponse,
        List<string> toolCalls,
        int depth,
        List<Decision> recentDecisions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GOAL: Evaluate the quality of this branch and improve it.");
        sb.AppendLine();
        sb.AppendLine($"Branch depth: {depth}");
        sb.AppendLine();
        sb.AppendLine("Original task:");
        sb.AppendLine(originalTask.Content);
        sb.AppendLine();
        sb.AppendLine("Branch final answer:");
        sb.AppendLine(branchResponse.Content);
        sb.AppendLine();

        if (toolCalls.Count > 0)
        {
            sb.AppendLine("Tool calls in branch:");
            foreach (var call in toolCalls.Take(20))
            {
                sb.AppendLine($"- {call}");
            }

            sb.AppendLine();
        }

        if (recentDecisions.Count > 0)
        {
            sb.AppendLine("Recent decisions created by this agent:");
            foreach (var d in recentDecisions)
            {
                sb.AppendLine($"- Context: {d.Context}");
                sb.AppendLine($"  Action: {d.Action}");
                sb.AppendLine($"  Reasoning: {d.Reasoning}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("Return strict JSON only (no markdown). Follow the schema defined in your system prompt.");
        return sb.ToString();
    }

    private static bool ContainsAnyKeyword(string? text, List<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(text) || keywords.Count == 0)
        {
            return false;
        }

        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseCritiqueJson(string json, out CritiqueResult? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = doc.RootElement;
            result = new CritiqueResult
            {
                QualityScore = TryGetInt(root, "quality_score", "qualityScore"),
                ShouldRollback = TryGetBool(root, "should_rollback", "shouldRollback"),
                ShouldKillBranch = TryGetBool(root, "should_kill_branch", "shouldKillBranch"),
                ImprovedSummary = TryGetString(root, "improved_summary", "improvedSummary"),
                Contradictions = TryGetStringList(root, "contradictions"),
                MissingSources = TryGetStringList(root, "missing_sources", "missingSources"),
                Recommendations = TryGetStringList(root, "recommendations")
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int TryGetInt(JsonElement obj, string snakeCase, string camelCase)
    {
        if (TryGetProperty(obj, snakeCase, out var el) || TryGetProperty(obj, camelCase, out el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            {
                return n;
            }

            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out n))
            {
                return n;
            }
        }

        return 0;
    }

    private static bool TryGetBool(JsonElement obj, string snakeCase, string camelCase)
    {
        if (TryGetProperty(obj, snakeCase, out var el) || TryGetProperty(obj, camelCase, out el))
        {
            if (el.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (el.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b))
            {
                return b;
            }
        }

        return false;
    }

    private static string? TryGetString(JsonElement obj, string snakeCase, string camelCase)
    {
        if (TryGetProperty(obj, snakeCase, out var el) || TryGetProperty(obj, camelCase, out el))
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }

        return null;
    }

    private static List<string>? TryGetStringList(JsonElement obj, string property)
        => TryGetStringList(obj, property, camelCase: property);

    private static List<string>? TryGetStringList(JsonElement obj, string snakeCase, string camelCase)
    {
        if (!TryGetProperty(obj, snakeCase, out var el) && !TryGetProperty(obj, camelCase, out el))
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s);
                }
            }
        }

        return list;
    }

    private static bool TryGetProperty(JsonElement obj, string propertyName, out JsonElement value)
    {
        if (obj.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        // Be defensive with case differences.
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InfernalHierarchy.Agents.Collaboration;
using InfernalHierarchy.Agents.Registry;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Messaging.Federation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using InfernalHierarchy.Tools.Notifications;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Notifications;

var baselinePath = Path.Combine(AppContext.BaseDirectory, "perf-baseline.json");
if (!File.Exists(baselinePath))
{
    baselinePath = Path.Combine(Directory.GetCurrentDirectory(), "perf-baseline.json");
}

if (!File.Exists(baselinePath))
{
    Console.Error.WriteLine("perf-baseline.json not found.");
    return 2;
}

var baseline = JsonSerializer.Deserialize<PerfBaseline>(
    await File.ReadAllTextAsync(baselinePath).ConfigureAwait(false),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
if (baseline is null)
{
    Console.Error.WriteLine("Failed to parse perf-baseline.json.");
    return 2;
}

var authorization = RunToolAuthorizationScenario();
var federation = await RunFederationScenarioAsync().ConfigureAwait(false);
var collaboration = await RunLocalCollaborationScenarioAsync().ConfigureAwait(false);
var capabilityGapPlanning = await RunCapabilityGapPlanningScenarioAsync().ConfigureAwait(false);
var capabilityGapRemediation = await RunCapabilityGapRemediationScenarioAsync().ConfigureAwait(false);
var inboxQuery = await RunInboxQueryToolScenarioAsync().ConfigureAwait(false);

PrintResult("toolAuthorization", authorization, baseline.ToolAuthorization);
PrintResult("federationAggregation", federation, baseline.FederationAggregation);
PrintResult("localCollaboration", collaboration, baseline.LocalCollaboration);
PrintResult("capabilityGapPlanning", capabilityGapPlanning, baseline.CapabilityGapPlanning);
PrintResult("capabilityGapRemediation", capabilityGapRemediation, baseline.CapabilityGapRemediation);
PrintResult("inboxQuery", inboxQuery, baseline.InboxQuery);

var failures = new List<string>();
Evaluate("toolAuthorization", authorization, baseline.ToolAuthorization, failures);
Evaluate("federationAggregation", federation, baseline.FederationAggregation, failures);
Evaluate("localCollaboration", collaboration, baseline.LocalCollaboration, failures);
Evaluate("capabilityGapPlanning", capabilityGapPlanning, baseline.CapabilityGapPlanning, failures);
Evaluate("capabilityGapRemediation", capabilityGapRemediation, baseline.CapabilityGapRemediation, failures);
Evaluate("inboxQuery", inboxQuery, baseline.InboxQuery, failures);

if (failures.Count == 0)
{
    Console.WriteLine("PERF_GATE:PASS");
    return 0;
}

Console.Error.WriteLine("PERF_GATE:FAIL");
foreach (var failure in failures)
{
    Console.Error.WriteLine($" - {failure}");
}

return 1;

static PerfResult RunToolAuthorizationScenario()
{
    var configValues = new Dictionary<string, string?>
    {
        ["ExecutionProfiles:Enabled"] = "true",
        ["ExecutionProfiles:DefaultProfile"] = "Build",
        ["ExecutionProfiles:Profiles:Build:Enabled"] = "true",
        ["ExecutionProfiles:Profiles:Build:AllowedTools:0"] = "python_exec",
        ["ExecutionProfiles:Profiles:Build:AllowedTools:1"] = "fs_read",
        ["ExecutionProfiles:Profiles:Build:AllowedTools:2"] = "http_request",
        ["ExecutionProfiles:Profiles:Build:CommandAllowlist:0"] = "python_exec",
        ["ExecutionProfiles:Profiles:Build:CommandAllowlist:1"] = "python",
        ["ExecutionProfiles:Profiles:Build:AllowedFileScopes:0"] = "src/**",
        ["ExecutionProfiles:Profiles:Build:AllowedNetworkScopes:0"] = "https://api.github.com"
    };

    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(configValues)
        .Build();

    var service = new ToolAuthorizationService(NullLogger<ToolAuthorizationService>.Instance, config);

    var parameters = new Dictionary<string, object>
    {
        ["working_dir"] = "src/InfernalHierarchy.Host",
        ["command"] = "python"
    };

    const int warmup = 500;
    const int iterations = 20_000;

    for (var i = 0; i < warmup; i++)
    {
        _ = service.IsAuthorized("lucifer", "Lucifer", AgentRank.Supreme, "python_exec", "Build", parameters);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        _ = service.IsAuthorized("lucifer", "Lucifer", AgentRank.Supreme, "python_exec", "Build", parameters);
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations);
}

static async Task<PerfResult> RunFederationScenarioAsync()
{
    var handler = new StaticFederationHttpHandler();
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://federation.local")
    };

    var service = new FederationService(NullLogger<FederationService>.Instance, client, "local");

    for (var i = 0; i < 4; i++)
    {
        await service.RegisterInstanceAsync(new FederatedInstance
        {
            InstanceId = $"remote-{i}",
            Name = $"Remote-{i}",
            BaseUrl = "https://federation.local",
            IsActive = true,
            CurrentAgentCount = 1,
            MaxAgents = 10,
            CurrentLoad = 0.2
        }).ConfigureAwait(false);
    }

    var request = new CollaborationRequest
    {
        Id = Guid.NewGuid().ToString(),
        InitiatorAgentId = "local-agent",
        Task = "Select deployment strategy",
        Strategy = CollaborationStrategy.WeightedVoting,
        MinimumParticipants = 3,
        MinimumConfidence = 0.6,
        Timeout = TimeSpan.FromSeconds(2)
    };

    const int warmup = 20;
    const int iterations = 250;

    for (var i = 0; i < warmup; i++)
    {
        _ = await service.RequestCrossInstanceCollaborationAsync(request).ConfigureAwait(false);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        _ = await service.RequestCrossInstanceCollaborationAsync(request).ConfigureAwait(false);
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations);
}

static async Task<PerfResult> RunLocalCollaborationScenarioAsync()
{
    var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);
    registry.Register(new PerfAgent("p1", AgentRank.Duke));
    registry.Register(new PerfAgent("p2", AgentRank.Worker));

    AgentCollaborationService? service = null;
    var bus = new InlineCollaborationBus(message =>
    {
        if (service is null || message.Type != MessageType.CollaborationRequest)
        {
            return Task.CompletedTask;
        }

        var agentRank = string.Equals(message.ToAgentId, "p1", StringComparison.Ordinal)
            ? AgentRank.Duke
            : AgentRank.Worker;

        return service.SubmitResponseAsync(
            message.CorrelationId ?? string.Empty,
            new AgentResponse
            {
                AgentId = message.ToAgentId ?? "unknown",
                AgentRank = agentRank,
                Response = "APPROVE",
                Confidence = agentRank == AgentRank.Duke ? 0.88 : 0.84,
                Reasoning = "deterministic perf response",
                Timestamp = DateTime.UtcNow
            });
    });

    service = new AgentCollaborationService(
        NullLogger<AgentCollaborationService>.Instance,
        bus,
        registry);

    const int warmup = 20;
    const int iterations = 200;

    for (var i = 0; i < warmup; i++)
    {
        _ = await service.RequestCollaborationAsync(BuildPerfRequest(i)).ConfigureAwait(false);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        _ = await service.RequestCollaborationAsync(BuildPerfRequest(i + warmup)).ConfigureAwait(false);
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations);

    static CollaborationRequest BuildPerfRequest(int i)
    {
        return new CollaborationRequest
        {
            Id = $"perf-collab-{i}-{Guid.NewGuid():N}",
            InitiatorAgentId = "local-agent",
            Task = "Approve release",
            Strategy = CollaborationStrategy.WeightedVoting,
            MinimumParticipants = 2,
            MinimumConfidence = 0.6,
            Timeout = TimeSpan.FromMilliseconds(300),
            ParticipantAgentIds = ["p1", "p2"]
        };
    }
}

static async Task<PerfResult> RunCapabilityGapPlanningScenarioAsync()
{
    var analyzer = new InfernalHierarchy.Agents.ReAct.DefaultCapabilityGapAnalyzer();

    var context = new InfernalHierarchy.Agents.ReAct.ReActTaskProcessorContext(
        AgentId: "perf-agent",
        AgentName: "Perf",
        AgentRank: AgentRank.Duke,
        Persona: new Persona(),
        LlmClient: NullLlmClient.Instance,
        ToolRegistry: new InfernalHierarchy.Tools.Execution.ToolRegistry(NullLogger<InfernalHierarchy.Tools.Execution.ToolRegistry>.Instance),
        SharedMemory: new NullSharedMemory(),
        ActionParser: new NullActionParser(),
        ActionExecutor: new NullActionExecutor(),
        ReportGenerator: new NullReportGenerator(),
        PromptBuilder: new NullPromptBuilder(),
        LoopRunner: new NullLoopRunner(),
        ReActOptions: new InfernalHierarchy.Agents.ReAct.ReActOptions(),
        RagOptions: new InfernalHierarchy.Core.Configuration.RagOptions(),
        VectorMemory: null,
        CollaborationService: null,
        RuntimeSkillStore: null,
        EventSink: null,
        SetStatus: _ => { },
        BuildBaseContextAsync: (_, _) => Task.FromResult("perf"),
        Logger: NullLogger.Instance);

    var persona = new Persona
    {
        Name = "Perf",
        SystemPrompt = "Perf",
        AvailableTools = ["create_custom_tool", "request_skill_pack", "request_collaboration"]
    };

    var message = new AgentMessage
    {
        Content = "Check my mailbox inbox for mails from alerts@example.com and integrate with API if needed"
    };

    const int warmup = 50;
    const int iterations = 500;

    for (var i = 0; i < warmup; i++)
    {
        _ = await analyzer.AnalyzeAsync(context, message, persona, CancellationToken.None).ConfigureAwait(false);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        _ = await analyzer.AnalyzeAsync(context, message, persona, CancellationToken.None).ConfigureAwait(false);
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations);
}

static async Task<PerfResult> RunCapabilityGapRemediationScenarioAsync()
{
    var orchestrator = new InfernalHierarchy.Agents.ReAct.DefaultCapabilityRemediationOrchestrator();

    var context = new InfernalHierarchy.Agents.ReAct.ReActTaskProcessorContext(
        AgentId: "perf-agent",
        AgentName: "Perf",
        AgentRank: AgentRank.Duke,
        Persona: new Persona
        {
            Name = "Perf",
            SystemPrompt = "Perf",
            AvailableTools = ["create_custom_tool", "request_collaboration", "request_skill_pack"]
        },
        LlmClient: NullLlmClient.Instance,
        ToolRegistry: new PerfToolRegistry(),
        SharedMemory: new NullSharedMemory(),
        ActionParser: new NullActionParser(),
        ActionExecutor: new NullActionExecutor(),
        ReportGenerator: new NullReportGenerator(),
        PromptBuilder: new NullPromptBuilder(),
        LoopRunner: new NullLoopRunner(),
        ReActOptions: new InfernalHierarchy.Agents.ReAct.ReActOptions(),
        RagOptions: new InfernalHierarchy.Core.Configuration.RagOptions(),
        VectorMemory: null,
        CollaborationService: null,
        RuntimeSkillStore: null,
        EventSink: null,
        SetStatus: _ => { },
        BuildBaseContextAsync: (_, _) => Task.FromResult("perf"),
        Logger: NullLogger.Instance);

    var analysis = new InfernalHierarchy.Agents.ReAct.CapabilityGapAnalysisResult(
        Gaps:
        [
            new InfernalHierarchy.Agents.ReAct.CapabilityGap(
                Capability: "mailbox_read",
                ReasonCode: "missing_mailbox_read_tool",
                Description: "Need inbox reader",
                BlockedByProfile: false,
                SuggestedSkillPackId: null,
                SuggestedExecutionProfile: "Research")
        ],
        Remediations:
        [
            new InfernalHierarchy.Agents.ReAct.CapabilityRemediationAction(
                Kind: InfernalHierarchy.Agents.ReAct.CapabilityRemediationActionKind.CreateCustomTool,
                ReasonCode: "synthesize_custom_tool",
                Capability: "mailbox_read",
                Description: "create inbox tool",
                CustomToolName: "email_inbox_query",
                CustomToolRequirement: "read-only inbox query"),
            new InfernalHierarchy.Agents.ReAct.CapabilityRemediationAction(
                Kind: InfernalHierarchy.Agents.ReAct.CapabilityRemediationActionKind.EscalateCollaboration,
                ReasonCode: "request_collaboration_audit",
                Capability: "mailbox_read",
                Description: "run audit")
        ],
        Report: new InfernalHierarchy.Agents.ReAct.CapabilityGapReport(
            RequestedOutcome: "check inbox",
            MissingCapabilities: ["mailbox_read"],
            CandidateTools: ["email_inbox_query"],
            SecurityRiskClass: InfernalHierarchy.Agents.ReAct.CapabilitySecurityRiskClass.Medium,
            CanAutofix: true,
            BlockReasonCode: "missing_mailbox_read_tool"),
        Plan: new InfernalHierarchy.Agents.ReAct.CapabilityRemediationPlan(
            PlanId: "perf-remediation-plan",
            Steps: [],
            MaxAttempts: 3,
            MaxDurationSeconds: 120,
            PolicyGateAllowsAutofix: true));

    var task = new AgentMessage
    {
        Id = "perf-remediation-task",
        CorrelationId = "perf-remediation-correlation",
        FromAgentId = "tester",
        ToAgentId = "perf-agent",
        Type = MessageType.Task,
        Content = "check inbox"
    };

    const int warmup = 40;
    const int iterations = 500;

    for (var i = 0; i < warmup; i++)
    {
        _ = await orchestrator.ExecuteAsync(context, task, analysis, CancellationToken.None).ConfigureAwait(false);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        _ = await orchestrator.ExecuteAsync(context, task, analysis, CancellationToken.None).ConfigureAwait(false);
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations);
}

static async Task<PerfResult> RunInboxQueryToolScenarioAsync()
{
    var options = Options.Create(new EmailInboxQueryOptions
    {
        Enabled = true,
        Host = "imap.example.com",
        Port = 993,
        UseSsl = true,
        Username = "reader@example.com",
        Password = "secret",
        MaxResults = 10,
        TimeoutMs = 3000
    });

    var tool = new EmailInboxQueryTool(options, new PerfInboxQueryClient(), NullLogger<EmailInboxQueryTool>.Instance);
    var parameters = new Dictionary<string, object>
    {
        ["from"] = "alerts@example.com",
        ["unread_only"] = true,
        ["max_results"] = 5
    };

    const int warmup = 50;
    const int iterations = 1000;

    for (var i = 0; i < warmup; i++)
    {
        _ = await tool.ExecuteAsync(parameters, CancellationToken.None).ConfigureAwait(false);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        _ = await tool.ExecuteAsync(parameters, CancellationToken.None).ConfigureAwait(false);
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations);
}

static void Evaluate(string name, PerfResult result, PerfBudget budget, List<string> failures)
{
    if (result.LatencyPerOpMs > budget.MaxLatencyPerOpMs)
    {
        failures.Add($"{name}: latency/op {result.LatencyPerOpMs:F3}ms > budget {budget.MaxLatencyPerOpMs:F3}ms");
    }

    if (result.AllocatedBytesPerOp > budget.MaxAllocatedBytesPerOp)
    {
        failures.Add($"{name}: alloc/op {result.AllocatedBytesPerOp:F0}B > budget {budget.MaxAllocatedBytesPerOp:F0}B");
    }
}

static void PrintResult(string name, PerfResult result, PerfBudget budget)
{
    Console.WriteLine($"[{name}] latency/op={result.LatencyPerOpMs:F3}ms (budget <= {budget.MaxLatencyPerOpMs:F3}ms)");
    Console.WriteLine($"[{name}] alloc/op={result.AllocatedBytesPerOp:F0}B (budget <= {budget.MaxAllocatedBytesPerOp:F0}B)");
}

internal sealed class StaticFederationHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["Decision"] = "APPROVE",
            ["Confidence"] = 0.91,
            ["AgentId"] = "remote-agent",
            ["Reasoning"] = "validated"
        };

        var message = new FederatedMessage
        {
            Id = Guid.NewGuid().ToString(),
            SourceInstanceId = "remote",
            TargetInstanceId = "local",
            MessageType = FederatedMessageType.CollaborationRequest,
            CorrelationId = Guid.NewGuid().ToString(),
            Payload = payload
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(message)
        };

        return Task.FromResult(response);
    }
}

internal sealed record PerfResult(double LatencyPerOpMs, double AllocatedBytesPerOp);

internal sealed class PerfBaseline
{
    public PerfBudget ToolAuthorization { get; set; } = new();
    public PerfBudget FederationAggregation { get; set; } = new();
    public PerfBudget LocalCollaboration { get; set; } = new();
    public PerfBudget CapabilityGapPlanning { get; set; } = new();
    public PerfBudget CapabilityGapRemediation { get; set; } = new();
    public PerfBudget InboxQuery { get; set; } = new();
}

internal sealed class PerfBudget
{
    public double MaxLatencyPerOpMs { get; set; }
    public double MaxAllocatedBytesPerOp { get; set; }
}

internal sealed class InlineCollaborationBus : IMessageBus
{
    private readonly Func<AgentMessage, Task> _onPublish;

    public InlineCollaborationBus(Func<AgentMessage, Task> onPublish)
    {
        _onPublish = onPublish;
    }

    public Task PublishAsync(AgentMessage message, CancellationToken ct = default)
        => _onPublish(message);

    public async IAsyncEnumerable<AgentMessage> SubscribeAsync(string agentId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public async IAsyncEnumerable<AgentMessage> SubscribeToBroadcastsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}

internal sealed class PerfAgent : IAgent
{
    public PerfAgent(string id, AgentRank rank)
    {
        Id = id;
        Rank = rank;
        Name = id;
    }

    public string Id { get; }
    public string Name { get; }
    public AgentRank Rank { get; }
    public AgentStatus Status => AgentStatus.Idle;
    public Persona Persona { get; } = new()
    {
        Name = "perf",
        DemonTitle = "perf",
        SystemPrompt = "perf"
    };

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SuspendAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default)
        => Task.FromResult(new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = MessageType.Report,
            FromAgentId = Id,
            ToAgentId = task.FromAgentId,
            Content = "ok",
            CorrelationId = task.CorrelationId,
            Timestamp = DateTime.UtcNow
        });

    public bool CanCreateSubAgent(AgentRank targetRank) => false;
}

internal sealed class NullLlmClient : ILlmClient
{
    public static NullLlmClient Instance { get; } = new();

    public Task<string> GetCompletionAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
        => Task.FromResult("{}");

    public Task<string> GetSimpleCompletionAsync(string prompt, CancellationToken ct = default)
        => Task.FromResult("{}");
}

internal sealed class NullSharedMemory : ISharedMemory
{
    public Task AddDecisionAsync(Decision decision, CancellationToken ct = default) => Task.CompletedTask;
    public Task<Decision?> GetDecisionAsync(string id, CancellationToken ct = default) => Task.FromResult<Decision?>(null);
    public Task<IEnumerable<Decision>> GetRecentDecisionsAsync(int count = 10, CancellationToken ct = default) => Task.FromResult<IEnumerable<Decision>>(Array.Empty<Decision>());
    public Task<IEnumerable<Decision>> SearchDecisionsAsync(string query, CancellationToken ct = default) => Task.FromResult<IEnumerable<Decision>>(Array.Empty<Decision>());
    public Task DeleteDecisionAsync(string id, CancellationToken ct = default) => Task.CompletedTask;

    public Task AddFactAsync(Fact fact, CancellationToken ct = default) => Task.CompletedTask;
    public Task<Fact?> GetFactAsync(string id, CancellationToken ct = default) => Task.FromResult<Fact?>(null);
    public Task<IEnumerable<Fact>> GetFactsByCategoryAsync(string category, CancellationToken ct = default) => Task.FromResult<IEnumerable<Fact>>(Array.Empty<Fact>());
    public Task<IEnumerable<Fact>> SearchFactsAsync(string query, CancellationToken ct = default) => Task.FromResult<IEnumerable<Fact>>(Array.Empty<Fact>());
    public Task UpdateFactAsync(Fact fact, string changeReason, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IEnumerable<FactVersion>> GetFactHistoryAsync(string factId, CancellationToken ct = default) => Task.FromResult<IEnumerable<FactVersion>>(Array.Empty<FactVersion>());
    public Task DeleteFactAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IEnumerable<Fact>> GetVisibleFactsAsync(string requestingAgentId, AgentRank requestingAgentRank, CancellationToken ct = default) => Task.FromResult<IEnumerable<Fact>>(Array.Empty<Fact>());
    public Task<IEnumerable<Fact>> SearchVisibleFactsAsync(string query, string requestingAgentId, AgentRank requestingAgentRank, CancellationToken ct = default) => Task.FromResult<IEnumerable<Fact>>(Array.Empty<Fact>());

    public Task AddTaskAsync(TaskEntry task, CancellationToken ct = default) => Task.CompletedTask;
    public Task<TaskEntry?> GetTaskAsync(string id, CancellationToken ct = default) => Task.FromResult<TaskEntry?>(null);
    public Task UpdateTaskAsync(TaskEntry task, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IEnumerable<TaskEntry>> GetTasksByStatusAsync(InfernalHierarchy.Core.Entities.TaskStatus status, CancellationToken ct = default) => Task.FromResult<IEnumerable<TaskEntry>>(Array.Empty<TaskEntry>());
    public Task<IEnumerable<TaskEntry>> GetTasksByAgentAsync(string agentId, CancellationToken ct = default) => Task.FromResult<IEnumerable<TaskEntry>>(Array.Empty<TaskEntry>());
    public Task DeleteTaskAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class NullActionParser : InfernalHierarchy.Agents.ReAct.IActionParser
{
    public bool TryParse(string response, bool useJsonResponse, out InfernalHierarchy.Agents.ReAct.ParsedAction parsed)
    {
        parsed = new InfernalHierarchy.Agents.ReAct.ParsedAction("", "FINAL_ANSWER", "", null);
        return true;
    }
}

internal sealed class NullActionExecutor : InfernalHierarchy.Agents.ReAct.IActionExecutor
{
    public Task<InfernalHierarchy.Agents.ReAct.ActionExecutionResult> ExecuteAsync(InfernalHierarchy.Agents.ReAct.ActionExecutionContext context)
        => Task.FromResult(new InfernalHierarchy.Agents.ReAct.ActionExecutionResult(true, true, "Observation: noop", "noop", null));
}

internal sealed class NullReportGenerator : InfernalHierarchy.Agents.ReAct.IReportGenerator
{
    public Task<string> GenerateUsageReportAsync(CancellationToken ct) => Task.FromResult("usage");
    public Task<string> GenerateModelsReportAsync(CancellationToken ct) => Task.FromResult("models");
}

internal sealed class NullPromptBuilder : InfernalHierarchy.Agents.ReAct.IReActPromptBuilder
{
    public string BuildPrompt(string systemContext, string conversationHistory, IReadOnlyCollection<string> availableTools, bool useJsonResponse)
        => string.Empty;
}

internal sealed class NullLoopRunner : InfernalHierarchy.Agents.ReAct.IReActLoopRunner
{
    public Task<InfernalHierarchy.Agents.ReAct.ReActLoopResult> RunAsync(InfernalHierarchy.Agents.ReAct.ReActLoopContext context, CancellationToken ct)
        => Task.FromResult(new InfernalHierarchy.Agents.ReAct.ReActLoopResult("ok", "ok", 1, Array.Empty<string>()));
}

internal sealed class PerfToolRegistry : IToolRegistry
{
    public void RegisterTool(ITool tool)
    {
    }

    public bool UnregisterTool(string name) => true;

    public ITool? GetTool(string name) => null;

    public IEnumerable<ITool> GetAllTools() => Array.Empty<ITool>();

    public IEnumerable<ITool> GetToolsForAgent(string[] toolNames) => Array.Empty<ITool>();

    public Task<ToolResult> ExecuteToolWithTrackingAsync(
        string toolName,
        Dictionary<string, object> parameters,
        string? agentId = null,
        string? agentRank = null,
        string? agentName = null,
        CancellationToken ct = default)
    {
        if (string.Equals(toolName, "request_collaboration", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ToolResult
            {
                Success = true,
                Output = "research.md\ndesign.json\ntest-report.json\nsecurity-report.json"
            });
        }

        return Task.FromResult(new ToolResult
        {
            Success = true,
            Output = "ok"
        });
    }
}

internal sealed class PerfInboxQueryClient : IEmailInboxQueryClient
{
    private static readonly IReadOnlyList<EmailInboxMessageSummary> Messages =
    [
        new EmailInboxMessageSummary("m1", "Alerts <alerts@example.com>", "Build status", DateTimeOffset.UtcNow.AddMinutes(-10), true),
        new EmailInboxMessageSummary("m2", "Alerts <alerts@example.com>", "Incident digest", DateTimeOffset.UtcNow.AddMinutes(-30), true),
        new EmailInboxMessageSummary("m3", "Ops <ops@example.com>", "Daily report", DateTimeOffset.UtcNow.AddHours(-2), false)
    ];

    public Task<IReadOnlyList<EmailInboxMessageSummary>> QueryAsync(
        EmailInboxQueryOptions options,
        EmailInboxQueryRequest request,
        CancellationToken ct = default)
    {
        return Task.FromResult(Messages);
    }
}

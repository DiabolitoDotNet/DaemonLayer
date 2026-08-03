using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InfernalHierarchy.Agents.Collaboration;
using InfernalHierarchy.Agents.Registry;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Eventing;
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
var perfGatePassToken = "PERF_GATE:PASS";
var perfGateFailToken = "PERF_GATE:FAIL";
var evidenceOutputPath = TryReadArgument(args, "--evidence-out");
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
var capabilityGapRemediationConcurrent = await RunCapabilityGapRemediationConcurrentScenarioAsync().ConfigureAwait(false);
var inboxQuery = await RunInboxQueryToolScenarioAsync().ConfigureAwait(false);
var autonomySloIntegration = await RunAutonomySloIntegrationScenarioAsync().ConfigureAwait(false);
var readinessScale = await RunReadinessScaleScenarioAsync().ConfigureAwait(false);
var autonomyScorecardReport = RunAutonomyScorecardReportScenario();
var autonomySoakStability = await RunAutonomySoakStabilityScenarioAsync().ConfigureAwait(false);
var autonomyCertificationTailLatency = RunAutonomyCertificationTailLatencyScenario();

PrintResult("toolAuthorization", authorization, baseline.ToolAuthorization);
PrintResult("federationAggregation", federation, baseline.FederationAggregation);
PrintResult("localCollaboration", collaboration, baseline.LocalCollaboration);
PrintResult("capabilityGapPlanning", capabilityGapPlanning, baseline.CapabilityGapPlanning);
PrintResult("capabilityGapRemediation", capabilityGapRemediation, baseline.CapabilityGapRemediation);
PrintResult("capabilityGapRemediationConcurrent", capabilityGapRemediationConcurrent, baseline.CapabilityGapRemediationConcurrent);
PrintResult("inboxQuery", inboxQuery, baseline.InboxQuery);
PrintResult("autonomySloIntegration", autonomySloIntegration, baseline.AutonomySloIntegration);
PrintResult("readinessScale", readinessScale, baseline.ReadinessScale);
PrintResult("autonomyScorecardReport", autonomyScorecardReport, baseline.AutonomyScorecardReport);
PrintResult("autonomySoakStability", autonomySoakStability, baseline.AutonomySoakStability);
PrintResult("autonomyCertificationTailLatency", autonomyCertificationTailLatency, baseline.AutonomyCertificationTailLatency);

var failures = new List<string>();
Evaluate("toolAuthorization", authorization, baseline.ToolAuthorization, failures);
Evaluate("federationAggregation", federation, baseline.FederationAggregation, failures);
Evaluate("localCollaboration", collaboration, baseline.LocalCollaboration, failures);
Evaluate("capabilityGapPlanning", capabilityGapPlanning, baseline.CapabilityGapPlanning, failures);
Evaluate("capabilityGapRemediation", capabilityGapRemediation, baseline.CapabilityGapRemediation, failures);
Evaluate("capabilityGapRemediationConcurrent", capabilityGapRemediationConcurrent, baseline.CapabilityGapRemediationConcurrent, failures);
Evaluate("inboxQuery", inboxQuery, baseline.InboxQuery, failures);
Evaluate("autonomySloIntegration", autonomySloIntegration, baseline.AutonomySloIntegration, failures);
Evaluate("readinessScale", readinessScale, baseline.ReadinessScale, failures);
Evaluate("autonomyScorecardReport", autonomyScorecardReport, baseline.AutonomyScorecardReport, failures);
Evaluate("autonomySoakStability", autonomySoakStability, baseline.AutonomySoakStability, failures);
Evaluate("autonomyCertificationTailLatency", autonomyCertificationTailLatency, baseline.AutonomyCertificationTailLatency, failures);

var evaluations = new List<PerfEvaluation>
{
    new("toolAuthorization", authorization, baseline.ToolAuthorization),
    new("federationAggregation", federation, baseline.FederationAggregation),
    new("localCollaboration", collaboration, baseline.LocalCollaboration),
    new("capabilityGapPlanning", capabilityGapPlanning, baseline.CapabilityGapPlanning),
    new("capabilityGapRemediation", capabilityGapRemediation, baseline.CapabilityGapRemediation),
    new("capabilityGapRemediationConcurrent", capabilityGapRemediationConcurrent, baseline.CapabilityGapRemediationConcurrent),
    new("inboxQuery", inboxQuery, baseline.InboxQuery),
    new("autonomySloIntegration", autonomySloIntegration, baseline.AutonomySloIntegration),
    new("readinessScale", readinessScale, baseline.ReadinessScale),
    new("autonomyScorecardReport", autonomyScorecardReport, baseline.AutonomyScorecardReport),
    new("autonomySoakStability", autonomySoakStability, baseline.AutonomySoakStability),
    new("autonomyCertificationTailLatency", autonomyCertificationTailLatency, baseline.AutonomyCertificationTailLatency),
};

WriteEvidenceIfRequested(evidenceOutputPath, evaluations, failures);

if (failures.Count == 0)
{
    Console.WriteLine(perfGatePassToken);
    return 0;
}

Console.Error.WriteLine(perfGateFailToken);
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

static async Task<PerfResult> RunCapabilityGapRemediationConcurrentScenarioAsync()
{
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
            PlanId: "perf-remediation-concurrent-plan",
            Steps: [],
            MaxAttempts: 3,
            MaxDurationSeconds: 120,
            PolicyGateAllowsAutofix: true));

    const int warmupBatches = 10;
    const int measuredBatches = 80;
    const int parallelism = 6;

    for (var i = 0; i < warmupBatches; i++)
    {
        var tasks = new List<Task>(parallelism);
        for (var p = 0; p < parallelism; p++)
        {
            tasks.Add(RunSingleRemediationAsync(analysis));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < measuredBatches; i++)
    {
        var tasks = new List<Task>(parallelism);
        for (var p = 0; p < parallelism; p++)
        {
            tasks.Add(RunSingleRemediationAsync(analysis));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);
    var operations = measuredBatches * parallelism;

    return new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / operations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)operations);

    static async Task RunSingleRemediationAsync(InfernalHierarchy.Agents.ReAct.CapabilityGapAnalysisResult analysis)
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

        var task = new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            CorrelationId = Guid.NewGuid().ToString("N"),
            FromAgentId = "tester",
            ToAgentId = "perf-agent",
            Type = MessageType.Task,
            Content = "check inbox"
        };

        _ = await orchestrator.ExecuteAsync(context, task, analysis, CancellationToken.None).ConfigureAwait(false);
    }
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

static Task<PerfResult> RunAutonomySloIntegrationScenarioAsync()
{
    using var eventStore = new InfernalHierarchy.Core.Eventing.EventStore(
        Path.Combine(Path.GetTempPath(), $"infernal_perf_events_{Guid.NewGuid():N}"),
        NullLogger<InfernalHierarchy.Core.Eventing.EventStore>.Instance);

    var metrics = new InfernalHierarchy.Host.Observability.MetricsCollector();
    var sink = new InfernalHierarchy.Host.Observability.CapabilityGapMetricsEventSink(eventStore, metrics);
    var failedStore = new PerfFailedOperationStore();
    var bus = new PerfMessageBus();
    var evaluator = new InfernalHierarchy.Host.Observability.SloGateEvaluator(metrics, failedStore, bus);

    var options = new InfernalHierarchy.Host.Observability.SloGateOptions
    {
        Enabled = true,
        MaxDeadLetterBacklogGrowth = 1000,
        MinReplaySamples = int.MaxValue,
        MinQueueSamples = int.MaxValue,
        MinTaskCompletionSamples = int.MaxValue,
        MinAutonomyTaskSamples = 1,
        MinAutonomyReplaySamples = 1,
        MinAutonomyTerminalSamples = 1,
        MinAutonomyTaskCompletionRatio = 0.5,
        MaxAutonomyTerminalFailureRatio = 0.5,
        MinAutonomyReplaySuccessRatio = 0.5,
        MaxAutonomyMedianTimeToTerminalMs = 1000
    };

    const int warmup = 20;
    const int iterations = 200;

    for (var i = 0; i < warmup; i++)
    {
        EmitAutonomyEvents(sink, i);
        _ = evaluator.Evaluate(options);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        EmitAutonomyEvents(sink, i + warmup);
        var result = evaluator.Evaluate(options);
        if (!result.Passed)
        {
            throw new InvalidOperationException("Autonomy SLO integration perf scenario produced an unexpected failed gate.");
        }
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return Task.FromResult(new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations));

    static void EmitAutonomyEvents(InfernalHierarchy.Host.Observability.CapabilityGapMetricsEventSink sink, int i)
    {
        sink.AppendEvent(new AgentEvent
        {
            AgentId = "perf-agent",
            Type = EventType.DecisionMade,
            Description = "Gap workflow resolved",
            Metadata = new Dictionary<string, object>
            {
                ["category"] = "capability.gap_analysis",
                ["workflow_state"] = i % 2 == 0
                    ? "capability_gap_resolved_retrying_original_intent"
                    : "capability_gap_unresolved_terminal",
                ["remediation_attempted"] = true,
                ["autofix_success"] = i % 2 == 0,
                ["remediation_duration_ms"] = 50d + (i % 5)
            }
        });

        sink.AppendEvent(new AgentEvent
        {
            AgentId = "perf-agent",
            Type = EventType.DecisionMade,
            Description = "Replay outcome",
            Metadata = new Dictionary<string, object>
            {
                ["category"] = "capability.replay",
                ["status"] = "success",
                ["attempts"] = 1
            }
        });
    }
}

static Task<PerfResult> RunReadinessScaleScenarioAsync()
{
    var registry = new ReadinessPerfToolRegistry(
        "request_collaboration",
        "workflow_step",
        "email_inbox_query",
        "email_send",
        "send_telegram");

    var options = Options.Create(new InfernalHierarchy.Host.Configuration.AutonomyReadinessOptions
    {
        Enabled = true,
        CatalogVersion = "perf",
        FailStartupOnCriticalNotReady = false,
        CriticalCapabilities =
        [
            "request_collaboration",
            "workflow_step",
            "email_inbox_query",
            "email_send",
            "send_telegram"
        ]
    });

    var inboxOptions = Options.Create(new EmailInboxQueryOptions
    {
        Enabled = true,
        Host = "imap.example.com",
        Username = "reader@example.com",
        Password = "secret"
    });

    var emailOptions = Options.Create(new EmailNotificationOptions
    {
        Enabled = true,
        Host = "smtp.example.com",
        Username = "sender@example.com",
        Password = "secret",
        FromAddress = "sender@example.com"
    });

    var telegramOptions = Options.Create(new InfernalHierarchy.Telegram.Options.TelegramOptions
    {
        BotToken = "token"
    });

    const int warmup = 50;
    const int iterations = 500;

    for (var i = 0; i < warmup; i++)
    {
        var store = new InfernalHierarchy.Host.Observability.AutonomyReadinessReportStore();
        var service = new InfernalHierarchy.Host.Hosting.AutonomyReadinessHostedService(
            NullLogger<InfernalHierarchy.Host.Hosting.AutonomyReadinessHostedService>.Instance,
            registry,
            options,
            inboxOptions,
            emailOptions,
            telegramOptions,
            store);

        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        var store = new InfernalHierarchy.Host.Observability.AutonomyReadinessReportStore();
        var service = new InfernalHierarchy.Host.Hosting.AutonomyReadinessHostedService(
            NullLogger<InfernalHierarchy.Host.Hosting.AutonomyReadinessHostedService>.Instance,
            registry,
            options,
            inboxOptions,
            emailOptions,
            telegramOptions,
            store);

        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return Task.FromResult(new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations));
}

static PerfResult RunAutonomyScorecardReportScenario()
{
    var playground = new InfernalHierarchy.Host.Tools.AgentPlaygroundService();
    var scorecard = new InfernalHierarchy.Host.Observability.AutonomyScorecardService(playground);

    SeedScorecardRuns(playground, "simple_search", "Research", 1200, successRate: 1.0);
    SeedScorecardRuns(playground, "missing_tool_task", "Research", 2400, successRate: 0.95);
    SeedScorecardRuns(playground, "multi_step_build", "Build", 4200, successRate: 0.90);
    SeedScorecardRuns(playground, "partial_failure_recovery", "Build", 2800, successRate: 0.85);

    const int warmup = 20;
    const int iterations = 300;

    for (var i = 0; i < warmup; i++)
    {
        _ = scorecard.GenerateReport(runsPerScenario: 50);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        var report = scorecard.GenerateReport(runsPerScenario: 50);
        if (report.Coverage <= 0)
        {
            throw new InvalidOperationException("Autonomy scorecard perf scenario returned empty coverage.");
        }
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations);

    static void SeedScorecardRuns(
        InfernalHierarchy.Host.Tools.AgentPlaygroundService playground,
        string benchmarkId,
        string profile,
        double durationMs,
        double successRate)
    {
        var scenarioId = playground.CreateScenario(
            name: $"perf-{benchmarkId}",
            prompt: "perf",
            toAgentId: "lucifer",
            timeoutMs: 20000,
            tags: new Dictionary<string, object>
            {
                ["benchmark_id"] = benchmarkId,
                ["execution_profile"] = profile
            });

        const int runs = 50;
        for (var i = 0; i < runs; i++)
        {
            var successful = i < Math.Round(runs * successRate);
            var payload = new Dictionary<string, object>
            {
                ["autonomy_outcome_autonomous_success"] = successful,
                ["autonomy_outcome_status"] = successful ? "success" : "non_autonomous_terminal"
            };

            var response = new InfernalHierarchy.Host.Api.ChatResponse(
                fromAgentId: "lucifer",
                toAgentId: "playground",
                content: successful ? "ok" : "fallback",
                payload: payload,
                correlationId: Guid.NewGuid().ToString("N"),
                causationId: null,
                receivedUtc: DateTime.UtcNow,
                durationMs: durationMs + (i % 7));

            _ = playground.AddRun(scenarioId, "perf", "lucifer", 20000, response);
        }
    }
}

static Task<PerfResult> RunAutonomySoakStabilityScenarioAsync()
{
    using var eventStore = new InfernalHierarchy.Core.Eventing.EventStore(
        Path.Combine(Path.GetTempPath(), $"infernal_perf_soak_events_{Guid.NewGuid():N}"),
        NullLogger<InfernalHierarchy.Core.Eventing.EventStore>.Instance);

    var metrics = new InfernalHierarchy.Host.Observability.MetricsCollector();
    var sink = new InfernalHierarchy.Host.Observability.CapabilityGapMetricsEventSink(eventStore, metrics);

    const int windows = 10;
    const int tasksPerWindow = 250;

    var completionRatios = new List<double>(windows);
    var terminalFailureRatios = new List<double>(windows);
    var medians = new List<double>(windows);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var w = 0; w < windows; w++)
    {
        for (var i = 0; i < tasksPerWindow; i++)
        {
            var globalIndex = (w * tasksPerWindow) + i;
            var isTransientFailure = globalIndex % 17 == 0;
            var isRecovered = isTransientFailure && globalIndex % 34 != 0;

            var workflowState = isTransientFailure && !isRecovered
                ? "capability_gap_unresolved_terminal"
                : "capability_gap_resolved_retrying_original_intent";

            sink.AppendEvent(new AgentEvent
            {
                AgentId = "soak-agent",
                Type = EventType.DecisionMade,
                Description = "Soak autonomy workflow",
                Metadata = new Dictionary<string, object>
                {
                    ["category"] = "capability.gap_analysis",
                    ["workflow_state"] = workflowState,
                    ["remediation_attempted"] = true,
                    ["autofix_success"] = !isTransientFailure || isRecovered,
                    ["remediation_duration_ms"] = 40d + (globalIndex % 11)
                }
            });

            if (isTransientFailure)
            {
                sink.AppendEvent(new AgentEvent
                {
                    AgentId = "soak-agent",
                    Type = EventType.DecisionMade,
                    Description = "Soak replay outcome",
                    Metadata = new Dictionary<string, object>
                    {
                        ["category"] = "capability.replay",
                        ["status"] = isRecovered ? "success" : "failed",
                        ["attempts"] = isRecovered ? 1 : 2
                    }
                });
            }
        }

        completionRatios.Add(metrics.GetGauge("autonomy_task_completion_ratio"));
        terminalFailureRatios.Add(metrics.GetGauge("autonomy_terminal_failure_ratio"));
        medians.Add(metrics.GetHistogramStats("autonomy.time_to_terminal_ms").P50);
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    static double Range(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var min = values.Min();
        var max = values.Max();
        return max - min;
    }

    var completionDrift = Range(completionRatios);
    var terminalFailureDrift = Range(terminalFailureRatios);
    var medianDrift = Range(medians);

    if (completionDrift > 0.10)
    {
        throw new InvalidOperationException($"Autonomy soak completion-ratio drift exceeded envelope: {completionDrift:F3}");
    }

    if (terminalFailureDrift > 0.10)
    {
        throw new InvalidOperationException($"Autonomy soak terminal-failure drift exceeded envelope: {terminalFailureDrift:F3}");
    }

    if (medianDrift > 30.0)
    {
        throw new InvalidOperationException($"Autonomy soak median-latency drift exceeded envelope: {medianDrift:F3}ms");
    }

    var totalIterations = windows * tasksPerWindow;
    return Task.FromResult(new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / totalIterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)totalIterations));
}

static PerfResult RunAutonomyCertificationTailLatencyScenario()
{
    var playground = new InfernalHierarchy.Host.Tools.AgentPlaygroundService();
    var scorecard = new InfernalHierarchy.Host.Observability.AutonomyScorecardService(playground);

    SeedRuns("simple_search", "Research", 1200, 1.0);
    SeedRuns("missing_tool_task", "Research", 2400, 0.95);
    SeedRuns("multi_step_build", "Build", 4200, 0.90);
    SeedRuns("partial_failure_recovery", "Build", 2800, 0.85);

    var options = new InfernalHierarchy.Host.Observability.AutonomyScorecardOptions
    {
        RunsPerScenario = 50,
        CertificationMode = true,
        FailOnInsufficientData = true,
        RequireStructuredOutcomeContract = true,
        MinCoverage = 1.0,
        MinGrade = "B",
        MinSuccessRatePerScenario = 0.8,
    };

    const int warmup = 20;
    const int iterations = 300;
    for (var i = 0; i < warmup; i++)
    {
        _ = scorecard.GenerateReport(options);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var latencies = new double[iterations];
    var beforeAlloc = GC.GetTotalAllocatedBytes(true);
    var sw = Stopwatch.StartNew();

    for (var i = 0; i < iterations; i++)
    {
        var iterationSw = Stopwatch.StartNew();
        var report = scorecard.GenerateReport(options);
        iterationSw.Stop();

        if (!report.CertificationPassed)
        {
            throw new InvalidOperationException("Autonomy certification tail-latency scenario produced an unexpected certification failure.");
        }

        latencies[i] = iterationSw.Elapsed.TotalMilliseconds;
    }

    sw.Stop();
    var afterAlloc = GC.GetTotalAllocatedBytes(true);

    return new PerfResult(
        LatencyPerOpMs: sw.Elapsed.TotalMilliseconds / iterations,
        AllocatedBytesPerOp: (afterAlloc - beforeAlloc) / (double)iterations,
        P95LatencyPerOpMs: CalculatePercentile(latencies, 95),
        P99LatencyPerOpMs: CalculatePercentile(latencies, 99));

    void SeedRuns(string benchmarkId, string profile, double durationMs, double successRate)
    {
        var scenarioId = playground.CreateScenario(
            name: $"cert-tail-{benchmarkId}",
            prompt: "perf",
            toAgentId: "lucifer",
            timeoutMs: 20000,
            tags: new Dictionary<string, object>
            {
                ["benchmark_id"] = benchmarkId,
                ["execution_profile"] = profile
            });

        const int runs = 50;
        for (var i = 0; i < runs; i++)
        {
            var successful = i < Math.Round(runs * successRate);
            var payload = new Dictionary<string, object>
            {
                ["autonomy_outcome_status"] = successful ? "success" : "non_autonomous_terminal",
                ["autonomy_outcome_reason_code"] = successful ? "success" : "non_autonomous_terminal",
                ["autonomy_outcome_autonomous_success"] = successful,
                ["autonomy_outcome_needs_supervisor_intervention"] = false,
                ["autonomy_outcome_next_action"] = "none",
                ["autonomy_scope_classification"] = "in_scope_autonomous",
                ["autonomy_scope_reason_code"] = "in_scope",
                ["autonomy_out_of_scope"] = false,
            };

            var response = new InfernalHierarchy.Host.Api.ChatResponse(
                fromAgentId: "lucifer",
                toAgentId: "playground",
                content: successful ? "ok" : "fallback",
                payload: payload,
                correlationId: Guid.NewGuid().ToString("N"),
                causationId: null,
                receivedUtc: DateTime.UtcNow,
                durationMs: durationMs + (i % 7));

            _ = playground.AddRun(scenarioId, "perf", "lucifer", 20000, response);
        }
    }
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

    if (budget.MaxP95LatencyPerOpMs is double maxP95 && result.P95LatencyPerOpMs is double p95 && p95 > maxP95)
    {
        failures.Add($"{name}: p95 latency/op {p95:F3}ms > budget {maxP95:F3}ms");
    }

    if (budget.MaxP99LatencyPerOpMs is double maxP99 && result.P99LatencyPerOpMs is double p99 && p99 > maxP99)
    {
        failures.Add($"{name}: p99 latency/op {p99:F3}ms > budget {maxP99:F3}ms");
    }
}

static void PrintResult(string name, PerfResult result, PerfBudget budget)
{
    Console.WriteLine($"[{name}] latency/op={result.LatencyPerOpMs:F3}ms (budget <= {budget.MaxLatencyPerOpMs:F3}ms)");
    Console.WriteLine($"[{name}] alloc/op={result.AllocatedBytesPerOp:F0}B (budget <= {budget.MaxAllocatedBytesPerOp:F0}B)");

    if (budget.MaxP95LatencyPerOpMs is double && result.P95LatencyPerOpMs is double p95)
    {
        Console.WriteLine($"[{name}] p95 latency/op={p95:F3}ms (budget <= {budget.MaxP95LatencyPerOpMs:F3}ms)");
    }

    if (budget.MaxP99LatencyPerOpMs is double && result.P99LatencyPerOpMs is double p99)
    {
        Console.WriteLine($"[{name}] p99 latency/op={p99:F3}ms (budget <= {budget.MaxP99LatencyPerOpMs:F3}ms)");
    }
}

static void WriteEvidenceIfRequested(string? evidenceOutputPath, IReadOnlyList<PerfEvaluation> evaluations, IReadOnlyList<string> failures)
{
    if (string.IsNullOrWhiteSpace(evidenceOutputPath))
    {
        return;
    }

    var fullPath = Path.GetFullPath(evidenceOutputPath);
    var dir = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrWhiteSpace(dir))
    {
        Directory.CreateDirectory(dir);
    }

    var payload = new
    {
        generatedAtUtc = DateTime.UtcNow,
        passed = failures.Count == 0,
        failures,
        scenarios = evaluations.Select(e => new
        {
            name = e.Name,
            result = new
            {
                latencyPerOpMs = e.Result.LatencyPerOpMs,
                allocatedBytesPerOp = e.Result.AllocatedBytesPerOp,
                p95LatencyPerOpMs = e.Result.P95LatencyPerOpMs,
                p99LatencyPerOpMs = e.Result.P99LatencyPerOpMs,
            },
            budget = new
            {
                maxLatencyPerOpMs = e.Budget.MaxLatencyPerOpMs,
                maxAllocatedBytesPerOp = e.Budget.MaxAllocatedBytesPerOp,
                maxP95LatencyPerOpMs = e.Budget.MaxP95LatencyPerOpMs,
                maxP99LatencyPerOpMs = e.Budget.MaxP99LatencyPerOpMs,
            }
        })
    };

    File.WriteAllText(fullPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PERF_EVIDENCE:{fullPath}");
}

static string? TryReadArgument(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 < args.Length)
            {
                return args[i + 1];
            }

            return null;
        }

        var prefix = name + "=";
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return arg[prefix.Length..];
        }
    }

    return null;
}

static double CalculatePercentile(IReadOnlyList<double> values, int percentile)
{
    if (values.Count == 0)
    {
        return 0;
    }

    var ordered = values.OrderBy(x => x).ToArray();
    var rank = Math.Clamp(percentile, 0, 100) / 100d * (ordered.Length - 1);
    var lower = (int)Math.Floor(rank);
    var upper = (int)Math.Ceiling(rank);
    if (lower == upper)
    {
        return ordered[lower];
    }

    var weight = rank - lower;
    return ordered[lower] + ((ordered[upper] - ordered[lower]) * weight);
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

internal sealed record PerfResult(
    double LatencyPerOpMs,
    double AllocatedBytesPerOp,
    double? P95LatencyPerOpMs = null,
    double? P99LatencyPerOpMs = null);

internal sealed record PerfEvaluation(string Name, PerfResult Result, PerfBudget Budget);

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by System.Text.Json during perf-baseline deserialization.")]
internal sealed class PerfBaseline
{
    public PerfBudget ToolAuthorization { get; set; } = new();
    public PerfBudget FederationAggregation { get; set; } = new();
    public PerfBudget LocalCollaboration { get; set; } = new();
    public PerfBudget CapabilityGapPlanning { get; set; } = new();
    public PerfBudget CapabilityGapRemediation { get; set; } = new();
    public PerfBudget CapabilityGapRemediationConcurrent { get; set; } = new();
    public PerfBudget InboxQuery { get; set; } = new();
    public PerfBudget AutonomySloIntegration { get; set; } = new();
    public PerfBudget ReadinessScale { get; set; } = new();
    public PerfBudget AutonomyScorecardReport { get; set; } = new();
    public PerfBudget AutonomySoakStability { get; set; } = new();
    public PerfBudget AutonomyCertificationTailLatency { get; set; } = new();
}

internal sealed class PerfBudget
{
    public double MaxLatencyPerOpMs { get; set; }
    public double MaxAllocatedBytesPerOp { get; set; }
    public double? MaxP95LatencyPerOpMs { get; set; }
    public double? MaxP99LatencyPerOpMs { get; set; }
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

internal sealed class PerfFailedOperationStore : IFailedOperationStore
{
    public Task RecordAsync(FailedOperationRecord record, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<FailedOperationRecord>> GetRecentAsync(int limit, bool pendingOnly, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FailedOperationRecord>>(Array.Empty<FailedOperationRecord>());

    public Task<FailedOperationRecord?> GetByIdAsync(string id, CancellationToken ct = default)
        => Task.FromResult<FailedOperationRecord?>(null);

    public Task<FailedOperationRecord?> TryStartReplayAsync(string id, string requestedBy, CancellationToken ct = default)
        => Task.FromResult<FailedOperationRecord?>(null);

    public Task MarkReplaySucceededAsync(string id, CancellationToken ct = default) => Task.CompletedTask;

    public Task MarkReplayFailedAsync(string id, string reasonCode, string? error, CancellationToken ct = default) => Task.CompletedTask;

    public FailedOperationStats GetStats() => new(Total: 0, Pending: 0, Replayed: 0, ReplayFailed: 0);
}

internal sealed class PerfMessageBus : IMessageBus
{
    public Task PublishAsync(AgentMessage message, CancellationToken ct = default) => Task.CompletedTask;

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

internal sealed class ReadinessPerfToolRegistry : IToolRegistry
{
    private readonly HashSet<string> _registered;

    public ReadinessPerfToolRegistry(params string[] registeredToolNames)
    {
        _registered = new HashSet<string>(registeredToolNames, StringComparer.OrdinalIgnoreCase);
    }

    public void RegisterTool(ITool tool)
    {
    }

    public bool UnregisterTool(string name) => true;

    public ITool? GetTool(string name)
        => _registered.Contains(name) ? DummyTool.Instance : null;

    public IEnumerable<ITool> GetAllTools() => Array.Empty<ITool>();

    public IEnumerable<ITool> GetToolsForAgent(string[] toolNames) => Array.Empty<ITool>();

    public Task<ToolResult> ExecuteToolWithTrackingAsync(
        string toolName,
        Dictionary<string, object> parameters,
        string? agentId = null,
        string? agentRank = null,
        string? agentName = null,
        CancellationToken ct = default)
        => Task.FromResult(new ToolResult { Success = true, Output = "ok" });

    private sealed class DummyTool : ITool
    {
        public static DummyTool Instance { get; } = new();

        public string Name => "dummy";
        public string Description => "dummy";

        public Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
            => Task.FromResult(new ToolResult { Success = true, Output = "ok" });
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Messaging.Federation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

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

PrintResult("toolAuthorization", authorization, baseline.ToolAuthorization);
PrintResult("federationAggregation", federation, baseline.FederationAggregation);

var failures = new List<string>();
Evaluate("toolAuthorization", authorization, baseline.ToolAuthorization, failures);
Evaluate("federationAggregation", federation, baseline.FederationAggregation, failures);

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
}

internal sealed class PerfBudget
{
    public double MaxLatencyPerOpMs { get; set; }
    public double MaxAllocatedBytesPerOp { get; set; }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests.E2E;

[Collection("Host E2E")]
public sealed class PerfPersonaDocsE2ETests
{
    private sealed class RequestProfilingEnabledFactory : InfernalHierarchyTestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Perf:RequestProfiling:Enabled"] = "true",
                    ["Perf:RequestProfiling:MaxRecords"] = "200",
                    ["Perf:RequestProfiling:RetentionMinutes"] = "5",
                });
            });
        }
    }

    private sealed class StrictSloGateFactory : InfernalHierarchyTestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SloGates:Enabled"] = "true",
                    ["SloGates:MaxDeadLetterBacklogGrowth"] = "0",
                    ["SloGates:MinReplaySuccessRatio"] = "1.0",
                    ["SloGates:MinReplaySamples"] = "1",
                    ["SloGates:MaxQueueRejectRate"] = "0.0",
                    ["SloGates:MinQueueSamples"] = "1",
                    ["SloGates:MaxTaskCompletionP95Ms"] = "100",
                    ["SloGates:MinTaskCompletionSamples"] = "1"
                });
            });
        }
    }

    [Theory]
    [InlineData("/ui/perf")]
    [InlineData("/ui/personas")]
    [InlineData("/ui/docs")]
    [InlineData("/ui/migrate")]
    public async Task Ui_Pages_ReturnHtml(string path)
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync(new Uri(path, UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain("InfernalHierarchy UI");
    }

    [Fact]
    public async Task Perf_Snapshot_ReturnsJson()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync(new Uri("/api/perf/snapshot", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("workingSetMB");
    }

    [Fact]
    public async Task Perf_HttpStats_ReturnsJson()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        // Prime the HTTP latency histograms.
        (await client.GetAsync(new Uri("/api/agents", UriKind.Relative))).EnsureSuccessStatusCode();

        var res = await client.GetAsync(new Uri("/api/perf/http", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("http.latency.get");
    }

    [Fact]
    public async Task Perf_Spans_ReturnsJson()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        // Generate at least one server Activity.
        (await client.GetAsync(new Uri("/api/agents", UriKind.Relative))).EnsureSuccessStatusCode();

        var res = await client.GetAsync(new Uri("/api/perf/spans", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("trace.span.");
    }

    [Fact]
    public async Task Perf_MessageBusDiagnostics_ReturnsActionablePayload()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        // Prime message bus activity through a normal API call.
        (await client.GetAsync(new Uri("/api/agents", UriKind.Relative))).EnsureSuccessStatusCode();

        var res = await client.GetAsync(new Uri("/api/perf/message-bus", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("supported").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("queue").TryGetProperty("capacity", out _).Should().BeTrue();
        doc.RootElement.GetProperty("backpressure").TryGetProperty("active", out _).Should().BeTrue();
        doc.RootElement.GetProperty("counters").TryGetProperty("deferredMessages", out _).Should().BeTrue();

        var recommendations = doc.RootElement.GetProperty("recommendations");
        recommendations.ValueKind.Should().Be(JsonValueKind.Array);
        recommendations.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Ops_DeadLetters_EndPoints_ReturnExpectedContract()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var operatorOptions = factory.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(operatorOptions.ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-Infernal-Operator-Key");
            client.DefaultRequestHeaders.Add("X-Infernal-Operator-Key", operatorOptions.ApiKey);
        }

        var listRes = await client.GetAsync(new Uri("/api/ops/deadletters?limit=5&pendingOnly=true", UriKind.Relative));
        listRes.EnsureSuccessStatusCode();

        var listJson = await listRes.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listJson);

        listDoc.RootElement.TryGetProperty("stats", out _).Should().BeTrue();
        listDoc.RootElement.TryGetProperty("records", out var records).Should().BeTrue();
        records.ValueKind.Should().Be(JsonValueKind.Array);

        var replayRes = await client.PostAsync(new Uri("/api/ops/deadletters/nonexistent-id/replay", UriKind.Relative), content: null);
        replayRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var replayJson = await replayRes.Content.ReadAsStringAsync();
        replayJson.Should().Contain("not_available");
    }

    [Fact]
    public async Task Perf_SloGates_DefaultThresholds_ReturnsPass()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync(new Uri("/api/perf/slo-gates", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("passed").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("checks").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Perf_LlmOptimization_ReturnsTokenAndRoutingPayload()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync(new Uri("/api/perf/llm-optimization", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("generatedUtc", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("tokenOptimization", out var tokenOptimization).Should().BeTrue();
        tokenOptimization.TryGetProperty("items", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("routingFeedback", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Perf_AutonomyScorecard_ReturnsBenchmarksAndReport()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync(new Uri("/api/perf/autonomy-scorecard", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("generatedUtc", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("benchmarks", out var benchmarks).Should().BeTrue();
        benchmarks.ValueKind.Should().Be(JsonValueKind.Array);
        benchmarks.GetArrayLength().Should().BeGreaterOrEqualTo(4);

        doc.RootElement.TryGetProperty("report", out var report).Should().BeTrue();
        report.TryGetProperty("coverage", out _).Should().BeTrue();
        report.TryGetProperty("scenarios", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Perf_TimelineExplainability_ReturnsSummaryAndItems()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var operatorOptions = factory.Services.GetRequiredService<IOptions<OperatorApiOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(operatorOptions.ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-Infernal-Operator-Key");
            client.DefaultRequestHeaders.Add("X-Infernal-Operator-Key", operatorOptions.ApiKey);
        }

        var res = await client.GetAsync(new Uri("/api/perf/timeline/explainability", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("summary", out var summary).Should().BeTrue();
        summary.TryGetProperty("tool_or_skill_creation", out _).Should().BeTrue();
        summary.TryGetProperty("deadletter_replay", out _).Should().BeTrue();
        summary.TryGetProperty("execution_profile_switch", out _).Should().BeTrue();
        summary.TryGetProperty("branch_preempted", out _).Should().BeTrue();

        doc.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Perf_SloGates_StrictThresholds_ReturnsServiceUnavailable()
    {
        using var factory = new StrictSloGateFactory();
        var client = factory.CreateClient();

        var metrics = factory.Services.GetRequiredService<InfernalHierarchy.Host.Observability.MetricsCollector>();
        metrics.IncrementCounter("deadletter.replay.failed");
        metrics.RecordValue("http.latency.post.api.chat.ms", 500);

        var store = factory.Services.GetRequiredService<IFailedOperationStore>();
        await store.RecordAsync(new FailedOperationRecord
        {
            Kind = FailedOperationKind.MessagePublish,
            ReasonCode = "synthetic",
            OperationName = "synthetic_op",
            PayloadJson = "{}"
        });

        var bus = factory.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new InfernalHierarchy.Core.Entities.AgentMessage
        {
            FromAgentId = "synthetic",
            ToAgentId = "none",
            Type = InfernalHierarchy.Core.Entities.MessageType.Task,
            Content = "synthetic"
        });

        var res = await client.GetAsync(new Uri("/api/perf/slo-gates", UriKind.Relative));
        res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("passed").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("checks").EnumerateArray().Any(c => !c.GetProperty("passed").GetBoolean()).Should().BeTrue();
    }

    [Fact]
    public async Task Perf_Traces_List_Detail_And_Download_Work()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        // Generate at least one server Activity/trace.
        (await client.GetAsync(new Uri("/api/agents", UriKind.Relative))).EnsureSuccessStatusCode();

        string? traceId = null;
        for (var i = 0; i < 10 && string.IsNullOrWhiteSpace(traceId); i++)
        {
            var listRes = await client.GetAsync(new Uri("/api/perf/traces?limit=10", UriKind.Relative));
            listRes.EnsureSuccessStatusCode();
            var listJson = await listRes.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(listJson);
            if (doc.RootElement.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array
                && items.GetArrayLength() > 0)
            {
                traceId = items[0].GetProperty("traceId").GetString();
                break;
            }

            await Task.Delay(50);
        }

        traceId.Should().NotBeNullOrWhiteSpace("at least one trace should be captured by the in-memory trace store");

        // detail
        var detailRes = await client.GetAsync(new Uri($"/api/perf/traces/{traceId}", UriKind.Relative));
        detailRes.EnsureSuccessStatusCode();
        var detailText = await detailRes.Content.ReadAsStringAsync();
        detailText.Should().Contain("spans");

        // download
        var dlRes = await client.GetAsync(new Uri($"/api/perf/traces/{traceId}/download", UriKind.Relative));
        dlRes.EnsureSuccessStatusCode();
        var dlText = await dlRes.Content.ReadAsStringAsync();
        dlText.Should().Contain(traceId);

        // tree
        var treeRes = await client.GetAsync(new Uri($"/api/perf/traces/{traceId}/tree", UriKind.Relative));
        treeRes.EnsureSuccessStatusCode();
        var treeText = await treeRes.Content.ReadAsStringAsync();

        using var treeDoc = JsonDocument.Parse(treeText);
        treeDoc.RootElement.TryGetProperty("roots", out var roots).Should().BeTrue();
        roots.ValueKind.Should().Be(JsonValueKind.Array);
        roots.GetArrayLength().Should().BeGreaterThan(0);
        roots[0].TryGetProperty("spanId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Perf_TraceCapture_Status_And_Clear_Work()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        // Generate at least one server Activity/trace.
        (await client.GetAsync(new Uri("/api/agents", UriKind.Relative))).EnsureSuccessStatusCode();

        var statusRes = await client.GetAsync(new Uri("/api/perf/trace-capture", UriKind.Relative));
        statusRes.EnsureSuccessStatusCode();
        var statusJson = await statusRes.Content.ReadAsStringAsync();
        statusJson.Should().Contain("tracesStored");

        var clearRes = await client.PostAsync(new Uri("/api/perf/traces/clear", UriKind.Relative), content: null);
        clearRes.EnsureSuccessStatusCode();
        var clearJson = await clearRes.Content.ReadAsStringAsync();
        clearJson.Should().Contain("cleared");
    }

    [Fact]
    public async Task Perf_RequestProfiling_WhenEnabled_AddsProfilingHeader_And_IsQueryable()
    {
        using var factory = new RequestProfilingEnabledFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync(new Uri("/api/agents", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        res.Headers.TryGetValues("X-Request-Profile-Id", out var values).Should().BeTrue();
        values.Should().NotBeNull();
        var id = values!.FirstOrDefault();
        id.Should().NotBeNullOrWhiteSpace();

        // Ensure it appears in the recent list.
        string? listJson = null;
        for (var i = 0; i < 10; i++)
        {
            var listRes = await client.GetAsync(new Uri("/api/perf/requests?limit=25", UriKind.Relative));
            listRes.EnsureSuccessStatusCode();
            listJson = await listRes.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(id) && listJson.Contains(id, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            await Task.Delay(30);
        }

        listJson.Should().NotBeNull();
        listJson!.Should().Contain(id!);
    }

    [Fact]
    public async Task Agent_Migration_Export_And_Import_Work()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var agents = await client.GetFromJsonAsync<List<AgentDto>>(new Uri("/api/agents", UriKind.Relative));
        agents.Should().NotBeNull();
        agents!.Count.Should().BeGreaterThan(0);

        var agentId = agents[0].id;
        agentId.Should().NotBeNullOrWhiteSpace();

        var exportRes = await client.GetAsync(new Uri($"/api/agents/{agentId}/export?facts=0&tasks=0&decisions=0", UriKind.Relative));
        exportRes.EnsureSuccessStatusCode();
        var bundleJson = await exportRes.Content.ReadAsStringAsync();
        bundleJson.Should().Contain("bundleId");
        bundleJson.Should().Contain("personaJson");

        var importRes = await client.PostAsJsonAsync("/api/agents/import", new
        {
            bundleJson,
            personaNameOverride = "imported_test_agent",
            parentAgentId = (string?)null,
            agentRankOverride = (string?)null,
            startAgent = false,
            importFacts = false,
            importTasks = false,
            importDecisions = false,
            overwritePersona = true,
        });

        var importText = await importRes.Content.ReadAsStringAsync();
        importRes.IsSuccessStatusCode.Should().BeTrue($"Import failed: {(int)importRes.StatusCode} {importRes.StatusCode} | {importText}");
        importText.Should().Contain("agentId");
        importText.Should().Contain("imported_test_agent");
    }

    [Fact]
    public async Task Docs_Markdown_ReturnsText()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        var res = await client.GetAsync(new Uri("/api/docs/markdown", UriKind.Relative));
        res.EnsureSuccessStatusCode();

        var md = await res.Content.ReadAsStringAsync();
        md.Should().Contain("# InfernalHierarchy");
        md.Should().Contain("## Tools");
    }

    [Fact]
    public async Task Personas_List_Load_Validate_Save_Works()
    {
        using var factory = new InfernalHierarchyTestWebAppFactory();
        var client = factory.CreateClient();

        // list
        var listRes = await client.GetAsync(new Uri("/api/personas", UriKind.Relative));
        listRes.EnsureSuccessStatusCode();

        var listText = await listRes.Content.ReadAsStringAsync();
        listText.Should().Contain("testdemon");

        // load
        var getRes = await client.GetAsync(new Uri("/api/personas/testdemon", UriKind.Relative));
        getRes.EnsureSuccessStatusCode();

        var payload = await getRes.Content.ReadFromJsonAsync<PersonaGetResponse>();
        payload.Should().NotBeNull();
        payload!.json.Should().Contain("\"name\"");

        // validate (ok)
        var validateRes = await client.PostAsJsonAsync("/api/personas/testdemon/validate", new { json = payload.json });
        validateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // save (change title)
        var updatedJson = payload.json.Replace("\"Test Demon\"", "\"Test Demon Updated\"", StringComparison.Ordinal);
        var saveRes = await client.PutAsJsonAsync("/api/personas/testdemon", new { json = updatedJson });
        saveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var getRes2 = await client.GetAsync(new Uri("/api/personas/testdemon", UriKind.Relative));
        var text2 = await getRes2.Content.ReadAsStringAsync();
        text2.Should().Contain("Test Demon Updated");
    }

    private sealed record PersonaGetResponse(string name, string json);

    private sealed record AgentDto(string id, string name, string rank, string status);
}

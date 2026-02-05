using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class MetricsServiceTests
{
    [Fact]
    public static void MetricsService_RecordsCountersGaugesAndHistograms()
    {
        var collector = new MetricsCollector();
        var service = new MetricsService(collector);

        service.RecordAgentCreated("Duke");
        service.RecordMessageSent("Command");
        service.SetActiveAgentCount("Worker", 12);
        service.RecordToolLatency("MemoryRead", 10);
        service.RecordMessageLatency(42);
        service.RecordLlmTokens(128);

        var all = service.GetAllMetrics();

        all.Should().ContainKey("counter.agents.created.duke");
        all["counter.agents.created.duke"].Should().Be(1L);

        all.Should().ContainKey("counter.messages.sent.command");
        all["counter.messages.sent.command"].Should().Be(1L);

        all.Should().ContainKey("gauge.agents.active.worker");
        all["gauge.agents.active.worker"].Should().Be(12d);

        all.Should().ContainKey("gauge.system.uptime.seconds");
        all["gauge.system.uptime.seconds"].Should().BeOfType<double>().Which.Should().BeGreaterThan(0);

        service.GetMessageLatencyStats().Count.Should().Be(1);
        service.GetToolLatencyStats("MemoryRead").Count.Should().Be(1);
    }

    [Fact]
    public static void MetricsService_ShouldRecordAllMetricTypes()
    {
        var collector = new MetricsCollector();
        var service = new MetricsService(collector);

        service.RecordAgentCreated("Supreme");
        service.RecordAgentTerminated("Supreme");
        service.SetActiveAgentCount("Supreme", 1);

        service.RecordMessageSent("Event");
        service.RecordMessageReceived("Event");
        service.RecordMessageLatency(5);

        service.RecordToolExecution("Web_Search");
        service.RecordToolSuccess("Web_Search");
        service.RecordToolFailure("Web_Search");
        service.RecordToolLatency("Web_Search", 12.34);

        service.RecordLlmCall();
        service.RecordLlmTokens(7);
        service.RecordLlmLatency(99);
        service.RecordLlmError();

        service.RecordMemoryWrite("Fact");
        service.RecordMemoryRead("Fact");
        service.SetMemorySize(123);

        service.RecordError("Host");

        var all = service.GetAllMetrics();

        all["counter.agents.created.supreme"].Should().Be(1L);
        all["counter.agents.terminated.supreme"].Should().Be(1L);
        all["gauge.agents.active.supreme"].Should().Be(1d);

        all["counter.messages.sent.event"].Should().Be(1L);
        all["counter.messages.received.event"].Should().Be(1L);

        all["counter.tools.executed.web_search"].Should().Be(1L);
        all["counter.tools.success.web_search"].Should().Be(1L);
        all["counter.tools.failure.web_search"].Should().Be(1L);

        all["counter.llm.calls"].Should().Be(1L);
        all["counter.llm.tokens"].Should().Be(7L);
        all["counter.llm.errors"].Should().Be(1L);

        all["counter.memory.write.fact"].Should().Be(1L);
        all["counter.memory.read.fact"].Should().Be(1L);
        all["gauge.memory.database.size.bytes"].Should().Be(123d);

        all["counter.errors.host"].Should().Be(1L);

        service.GetMessageLatencyStats().Count.Should().BeGreaterThan(0);
        service.GetLlmLatencyStats().Count.Should().BeGreaterThan(0);
        service.GetToolLatencyStats("WEB_SEARCH").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public static void MetricsCollector_HistogramStats_ShouldComputePercentiles_AndTrimTo1000()
    {
        var collector = new MetricsCollector();

        collector.GetHistogramStats("missing").Count.Should().Be(0);

        collector.GetCounter("c").Should().Be(0);
        collector.IncrementCounter("c");
        collector.IncrementCounter("c", 2);
        collector.GetCounter("c").Should().Be(3);

        collector.GetGauge("g").Should().Be(0);
        collector.SetGauge("g", 1.5);
        collector.GetGauge("g").Should().Be(1.5);

        collector.RecordValue("h", 1);
        collector.RecordValue("h", 4);
        collector.RecordValue("h", 3);
        collector.RecordValue("h", 2);

        var stats = collector.GetHistogramStats("h");
        stats.Count.Should().Be(4);
        stats.Min.Should().Be(1);
        stats.Max.Should().Be(4);
        stats.P50.Should().Be(2);
        stats.P95.Should().Be(4);
        stats.P99.Should().Be(4);

        for (var i = 0; i < 1001; i++)
        {
            collector.RecordValue("trim", i + 1);
        }

        var trimmed = collector.GetHistogramStats("trim");
        trimmed.Count.Should().Be(1000);
        trimmed.Min.Should().Be(2);
        trimmed.Max.Should().Be(1001);

        var all = collector.GetAllMetrics();
        all.Keys.Should().Contain(k => k == "counter.c");
        all.Keys.Should().Contain(k => k == "gauge.g");
        all.Keys.Should().Contain(k => k == "histogram.h.count");

        collector.Reset();
        collector.GetAllMetrics().Should().BeEmpty();
    }
}

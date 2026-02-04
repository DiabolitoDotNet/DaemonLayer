using FluentAssertions;
using InfernalHierarchy.Host;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class PrometheusMetricsFormatterTests
{
    [Fact]
    public void Format_ShouldRenderCountersWithTotalSuffix_AndPrefix()
    {
        var metrics = new Dictionary<string, object>
        {
            ["counter.tools.executed.web_search"] = 3L,
            ["gauge.system.uptime.seconds"] = 12.5,
        };

        var output = PrometheusMetricsFormatter.Format(metrics, prefix: "infernal");

        output.Should().Contain("infernal_tools_executed_web_search_total 3");
        output.Should().Contain("infernal_system_uptime_seconds 12.5");
    }

    [Fact]
    public void Format_ShouldSanitizeMetricNames()
    {
        var metrics = new Dictionary<string, object>
        {
            ["counter.some.metric-with.dashes"] = 1,
            ["gauge.weird space"] = 2,
            ["histogram.tool.latency.web search.ms.p95"] = 123.45,
        };

        var output = PrometheusMetricsFormatter.Format(metrics, prefix: "ih");

        output.Should().Contain("ih_some_metric_with_dashes_total 1");
        output.Should().Contain("ih_weird_space 2");
        output.Should().Contain("ih_histogram_tool_latency_web_search_ms_p95 123.45");
    }

    [Fact]
    public void Format_ShouldSkipNonNumericValues()
    {
        var metrics = new Dictionary<string, object>
        {
            ["gauge.ok"] = 1,
            ["gauge.bad"] = new { a = 1 },
        };

        var output = PrometheusMetricsFormatter.Format(metrics);

        output.Should().Contain("infernal_ok 1");
        output.Should().NotContain("infernal_bad");
    }

    [Fact]
    public void Format_ShouldUseInvariantCulture_ForDecimals()
    {
        var metrics = new Dictionary<string, object>
        {
            ["gauge.pi"] = 3.14159,
        };

        var output = PrometheusMetricsFormatter.Format(metrics);

        output.Should().Contain("infernal_pi 3.14159");
    }
}

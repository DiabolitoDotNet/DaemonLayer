using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class PrometheusMetricsFormatterTests
{
    [Fact]
    public void Format_WithNullMetrics_ShouldThrow()
    {
        Action act = () => PrometheusMetricsFormatter.Format(null!);
        act.Should().Throw<ArgumentNullException>();
    }

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
    public void Format_ShouldTreatMetricPrefixesCaseInsensitive()
    {
        var metrics = new Dictionary<string, object>
        {
            ["Counter.Tools.Executed"] = 1,
            ["GAUGE.System.Up"] = 2,
        };

        var output = PrometheusMetricsFormatter.Format(metrics, prefix: "infernal");

        output.Should().Contain("infernal_Tools_Executed_total 1");
        output.Should().Contain("infernal_System_Up 2");
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

    [Fact]
    public void Format_ShouldConvertDecimals()
    {
        var metrics = new Dictionary<string, object>
        {
            ["gauge.dec"] = 1.25m,
        };

        var output = PrometheusMetricsFormatter.Format(metrics);

        output.Should().Contain("infernal_dec 1.25");
    }

    [Fact]
    public void Format_ShouldUseDefaultPrefix_WhenPrefixIsBlank()
    {
        var metrics = new Dictionary<string, object>
        {
            ["gauge.x"] = 1,
        };

        var output = PrometheusMetricsFormatter.Format(metrics, prefix: " ");

        output.Should().Contain("infernal_x 1");
    }

    [Fact]
    public void Format_ShouldParseNumericStrings_UsingInvariantCulture()
    {
        var metrics = new Dictionary<string, object>
        {
            ["gauge.from_string"] = "2.5",
        };

        var output = PrometheusMetricsFormatter.Format(metrics);

        output.Should().Contain("infernal_from_string 2.5");
    }

    [Fact]
    public void Format_ShouldNotParseNumericStrings_WithCommaDecimal()
    {
        var metrics = new Dictionary<string, object>
        {
            ["gauge.from_string"] = "2,5",
        };

        var output = PrometheusMetricsFormatter.Format(metrics);

        output.Should().NotContain("infernal_from_string");
    }

    [Fact]
    public void Format_ShouldInsertLeadingUnderscore_WhenPrefixStartsWithDigit()
    {
        var metrics = new Dictionary<string, object>
        {
            ["gauge.1abc"] = 1,
        };

        var output = PrometheusMetricsFormatter.Format(metrics, prefix: "1bad");

        output.Should().Contain("_1bad_1abc 1");
    }

    [Fact]
    public void Format_ShouldSkipWhitespaceKeys()
    {
        var metrics = new Dictionary<string, object>
        {
            [" "] = 1,
            ["gauge.ok"] = 2,
        };

        var output = PrometheusMetricsFormatter.Format(metrics);

        output.Should().Contain("infernal_ok 2");
        output.Should().NotContain("infernal_ 1");
    }
}

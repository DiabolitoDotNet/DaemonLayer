using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class MetricsCollectorTests
{
    private readonly MetricsCollector _sut;

    public MetricsCollectorTests()
    {
        _sut = new MetricsCollector();
    }

    [Fact]
    public void IncrementCounter_ShouldIncrementValue()
    {
        // Arrange
        var counterName = "test.counter";

        // Act
        _sut.IncrementCounter(counterName, 5);
        _sut.IncrementCounter(counterName, 3);

        // Assert
        _sut.GetCounter(counterName).Should().Be(8);
    }

    [Fact]
    public void IncrementCounter_WithDefaultValue_ShouldIncrementByOne()
    {
        // Arrange
        var counterName = "test.counter.default";

        // Act
        _sut.IncrementCounter(counterName);
        _sut.IncrementCounter(counterName);
        _sut.IncrementCounter(counterName);

        // Assert
        _sut.GetCounter(counterName).Should().Be(3);
    }

    [Fact]
    public void GetCounter_WithNonExistentCounter_ShouldReturnZero()
    {
        // Act
        var result = _sut.GetCounter("nonexistent.counter");

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void SetGauge_ShouldSetValue()
    {
        // Arrange
        var gaugeName = "test.gauge";

        // Act
        _sut.SetGauge(gaugeName, 42.5);

        // Assert
        _sut.GetGauge(gaugeName).Should().Be(42.5);
    }

    [Fact]
    public void SetGauge_ShouldOverwritePreviousValue()
    {
        // Arrange
        var gaugeName = "test.gauge.overwrite";

        // Act
        _sut.SetGauge(gaugeName, 10.0);
        _sut.SetGauge(gaugeName, 20.0);

        // Assert
        _sut.GetGauge(gaugeName).Should().Be(20.0);
    }

    [Fact]
    public void GetGauge_WithNonExistentGauge_ShouldReturnZero()
    {
        // Act
        var result = _sut.GetGauge("nonexistent.gauge");

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void RecordValue_ShouldAddValueToHistogram()
    {
        // Arrange
        var histogramName = "test.histogram";

        // Act
        _sut.RecordValue(histogramName, 10.0);
        _sut.RecordValue(histogramName, 20.0);
        _sut.RecordValue(histogramName, 30.0);

        // Assert
        var stats = _sut.GetHistogramStats(histogramName);
        stats.Count.Should().Be(3);
        stats.Min.Should().Be(10.0);
        stats.Max.Should().Be(30.0);
        stats.Mean.Should().BeApproximately(20.0, 0.1);
    }

    [Fact]
    public void GetHistogramStats_ShouldCalculatePercentiles()
    {
        // Arrange
        var histogramName = "test.percentiles";
        var values = Enumerable.Range(1, 100).Select(x => (double)x);

        // Act
        foreach (var value in values)
        {
            _sut.RecordValue(histogramName, value);
        }

        var stats = _sut.GetHistogramStats(histogramName);

        // Assert
        stats.P50.Should().BeApproximately(50.0, 5.0);
        stats.P95.Should().BeApproximately(95.0, 5.0);
        stats.P99.Should().BeApproximately(99.0, 5.0);
    }

    [Fact]
    public void GetHistogramStats_WithNoData_ShouldReturnEmptyStats()
    {
        // Act
        var stats = _sut.GetHistogramStats("nonexistent.histogram");

        // Assert
        stats.Count.Should().Be(0);
        stats.Min.Should().Be(0);
        stats.Max.Should().Be(0);
        stats.Mean.Should().Be(0);
    }

    [Fact]
    public void RecordValue_ShouldLimitHistogramSize()
    {
        // Arrange
        var histogramName = "test.limited";

        // Act - Record 1500 values (exceeds 1000 limit)
        for (int i = 0; i < 1500; i++)
        {
            _sut.RecordValue(histogramName, i);
        }

        // Assert - Should only keep last 1000 values
        var stats = _sut.GetHistogramStats(histogramName);
        stats.Count.Should().Be(1000);
        stats.Min.Should().BeGreaterThan(0); // First values should be removed
    }

    [Fact]
    public void GetAllMetrics_ShouldReturnAllMetrics()
    {
        // Arrange
        _sut.IncrementCounter("counter1", 10);
        _sut.SetGauge("gauge1", 25.5);
        _sut.RecordValue("histogram1", 100.0);

        // Act
        var allMetrics = _sut.GetAllMetrics();

        // Assert
        allMetrics.Should().ContainKey("counter.counter1");
        allMetrics.Should().ContainKey("gauge.gauge1");
        allMetrics["counter.counter1"].Should().Be(10L);
        allMetrics["gauge.gauge1"].Should().Be(25.5);
    }

    [Fact]
    public async Task MetricsCollector_ShouldBeThreadSafe()
    {
        // Arrange
        var counterName = "concurrent.counter";
        var tasks = new List<Task>();

        // Act - Increment counter from 100 threads concurrently
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => _sut.IncrementCounter(counterName, 1)));
        }

        await Task.WhenAll(tasks);

        // Assert
        _sut.GetCounter(counterName).Should().Be(100);
    }
}

public sealed class PerformanceMonitorTests : IDisposable
{
    private readonly Mock<ILogger<PerformanceMonitor>> _mockLogger;
    private readonly MetricsCollector _metricsCollector;
    private readonly PerformanceMonitor _sut;
    private bool _disposed;

    public PerformanceMonitorTests()
    {
        _mockLogger = new Mock<ILogger<PerformanceMonitor>>();
        _metricsCollector = new MetricsCollector();
        _sut = new PerformanceMonitor(_mockLogger.Object, _metricsCollector);
    }

    [Fact]
    public async Task PerformanceMonitor_ShouldUpdateMetricsPeriodically()
    {
        // Arrange - Wait for initial metrics update (5 seconds)
        await Task.Delay(6000);

        // Act
        var workingSet = _metricsCollector.GetGauge("system.memory.working_set.mb");
        var cpuUsage = _metricsCollector.GetGauge("system.cpu.usage.percent");
        var threadCount = _metricsCollector.GetGauge("system.threads.count");

        // Assert
        workingSet.Should().BeGreaterThan(0);
        threadCount.Should().BeGreaterThan(0);
        // CPU usage can be 0 in tests, so just check it's not negative
        cpuUsage.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetCurrentSnapshot_ShouldReturnValidSnapshot()
    {
        // Act
        var snapshot = _sut.GetCurrentSnapshot();

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.WorkingSetMB.Should().BeGreaterThan(0);
        snapshot.ThreadCount.Should().BeGreaterThan(0);
        snapshot.HandleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetCurrentSnapshot_ShouldIncludeMemoryMetrics()
    {
        // Act
        var snapshot = _sut.GetCurrentSnapshot();

        // Assert
        snapshot.WorkingSetMB.Should().BeGreaterThan(0);
        snapshot.PrivateMemoryMB.Should().BeGreaterThan(0);
        snapshot.GcTotalMemoryMB.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetCurrentSnapshot_ShouldIncludeGCMetrics()
    {
        // Arrange
        GC.Collect(0); // Force a Gen0 collection

        // Act
        var snapshot = _sut.GetCurrentSnapshot();

        // Assert
        snapshot.Gen0Collections.Should().BeGreaterThan(0);
        snapshot.Gen1Collections.Should().BeGreaterThanOrEqualTo(0);
        snapshot.Gen2Collections.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetCurrentSnapshot_CalledMultipleTimes_ShouldReturnUpdatedValues()
    {
        // Act
        var snapshot1 = _sut.GetCurrentSnapshot();

        // Update a deterministic input between calls.
        // GC memory metrics are inherently non-deterministic across runs (GC may run and memory can decrease).
        const double updatedCpuUsage = 42.42;
        _metricsCollector.SetGauge("system.cpu.usage.percent", updatedCpuUsage);

        var snapshot2 = _sut.GetCurrentSnapshot();

        // Assert
        snapshot2.CpuUsagePercent.Should().Be(updatedCpuUsage);
    }

    [Fact]
    public async Task PerformanceMonitor_ShouldUpdateGaugesInMetricsCollector()
    {
        // Arrange - Create fresh instances
        var logger = new Mock<ILogger<PerformanceMonitor>>();
        var collector = new MetricsCollector();
        using var monitor = new PerformanceMonitor(logger.Object, collector);

        // Act - Wait for metrics update
        await Task.Delay(6000);

        // Assert
        var memoryGauge = collector.GetGauge("system.memory.working_set.mb");
        memoryGauge.Should().BeGreaterThan(0);

        // Cleanup handled by using
    }

    [Fact]
    public void Dispose_ShouldStopMonitoring()
    {
        // Arrange
        var logger = new Mock<ILogger<PerformanceMonitor>>();
        var collector = new MetricsCollector();
        var monitor = new PerformanceMonitor(logger.Object, collector);

        // Act
        monitor.Dispose();

        // Assert - Access after disposal should be rejected
        Assert.Throws<ObjectDisposedException>(() => monitor.GetCurrentSnapshot());
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _sut.Dispose();
        }

        _disposed = true;
    }
}

public class DistributedTracingTests
{
    [Fact]
    public void ActivitySource_ShouldBeNamed()
    {
        // Arrange & Act
        using var activitySource = new ActivitySource("InfernalHierarchy", "1.0.0");

        // Assert
        activitySource.Name.Should().Be("InfernalHierarchy");
        activitySource.Version.Should().Be("1.0.0");
    }

    [Fact]
    public void Activity_ShouldRecordTags()
    {
        // Arrange
        using var activitySource = new ActivitySource("InfernalHierarchy");

        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        using var activity = activitySource.StartActivity("test.operation");
        activity?.SetTag("agent.id", "lucifer");
        activity?.SetTag("operation.type", "tool_execution");

        // Assert
        activity.Should().NotBeNull();
        activity?.GetTagItem("agent.id").Should().Be("lucifer");
        activity?.GetTagItem("operation.type").Should().Be("tool_execution");
    }

    [Fact]
    public void Activity_ShouldRecordError()
    {
        // Arrange
        using var activitySource = new ActivitySource("InfernalHierarchy");

        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        using var activity = activitySource.StartActivity("test.error");
        var exception = new InvalidOperationException("Test error");

        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddEvent(new ActivityEvent(
            "exception",
            tags: new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName ?? "unknown" },
                { "exception.message", exception.Message }
            }));

        // Assert
        activity.Should().NotBeNull();
        activity?.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void Activity_ShouldSupportNesting()
    {
        // Arrange
        using var activitySource = new ActivitySource("InfernalHierarchy");

        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        using var parentActivity = activitySource.StartActivity("parent.operation");
        var parentId = parentActivity?.Id;

        using var childActivity = activitySource.StartActivity("child.operation");
        var childParentId = childActivity?.ParentId;

        // Assert
        parentActivity.Should().NotBeNull();
        childActivity.Should().NotBeNull();
        childParentId.Should().Be(parentId);
    }
}

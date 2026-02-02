using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace InfernalHierarchy.Host;

/// <summary>
/// Performance monitoring service for system resources
/// </summary>
public class PerformanceMonitor : IDisposable
{
    private readonly ILogger<PerformanceMonitor> _logger;
    private readonly MetricsCollector _metricsCollector;
    private readonly Timer _monitoringTimer;
    private readonly Process _currentProcess;
    private long _lastTotalProcessorTime;
    private DateTime _lastMonitorTime;
    private bool _disposed;

    public PerformanceMonitor(ILogger<PerformanceMonitor> logger, MetricsCollector metricsCollector)
    {
        _logger = logger;
        _metricsCollector = metricsCollector;
        _currentProcess = Process.GetCurrentProcess();
        _lastMonitorTime = DateTime.UtcNow;
        _lastTotalProcessorTime = _currentProcess.TotalProcessorTime.Ticks;

        // Update metrics every 30 seconds
        _monitoringTimer = new Timer(UpdateMetrics, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    private void UpdateMetrics(object? state)
    {
        if (_disposed) return;

        try
        {
            // Memory metrics
            _currentProcess.Refresh();
            var workingSetMb = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);
            var privateMemoryMb = _currentProcess.PrivateMemorySize64 / (1024.0 * 1024.0);
            var gcTotalMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

            _metricsCollector.SetGauge("system.memory.working_set.mb", workingSetMb);
            _metricsCollector.SetGauge("system.memory.private.mb", privateMemoryMb);
            _metricsCollector.SetGauge("system.memory.gc.mb", gcTotalMemoryMb);

            // CPU metrics
            var currentTotalProcessorTime = _currentProcess.TotalProcessorTime.Ticks;
            var currentTime = DateTime.UtcNow;

            var cpuUsedTicks = currentTotalProcessorTime - _lastTotalProcessorTime;
            var totalTicksPassed = (currentTime - _lastMonitorTime).Ticks;

            if (totalTicksPassed > 0)
            {
                var cpuUsagePercent = (cpuUsedTicks / (double)totalTicksPassed) * 100.0;
                _metricsCollector.SetGauge("system.cpu.usage.percent", cpuUsagePercent);
            }

            _lastTotalProcessorTime = currentTotalProcessorTime;
            _lastMonitorTime = currentTime;

            // Thread metrics
            _metricsCollector.SetGauge("system.threads.count", _currentProcess.Threads.Count);

            // GC metrics
            _metricsCollector.SetGauge("system.gc.gen0_collections", GC.CollectionCount(0));
            _metricsCollector.SetGauge("system.gc.gen1_collections", GC.CollectionCount(1));
            _metricsCollector.SetGauge("system.gc.gen2_collections", GC.CollectionCount(2));

            // Handle metrics
            _metricsCollector.SetGauge("system.handles.count", _currentProcess.HandleCount);

            _logger.LogDebug(
                "Performance metrics updated - Memory: {Memory:F2}MB, CPU: {Cpu:F2}%, Threads: {Threads}",
                workingSetMb,
                _metricsCollector.GetGauge("system.cpu.usage.percent"),
                _currentProcess.Threads.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update performance metrics");
        }
    }

    public PerformanceSnapshot GetCurrentSnapshot()
    {
        _currentProcess.Refresh();
        return new PerformanceSnapshot
        {
            WorkingSetMB = _currentProcess.WorkingSet64 / (1024.0 * 1024.0),
            PrivateMemoryMB = _currentProcess.PrivateMemorySize64 / (1024.0 * 1024.0),
            GcTotalMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
            CpuUsagePercent = _metricsCollector.GetGauge("system.cpu.usage.percent"),
            ThreadCount = _currentProcess.Threads.Count,
            HandleCount = _currentProcess.HandleCount,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitoringTimer?.Dispose();
        _currentProcess?.Dispose();
    }
}

public class PerformanceSnapshot
{
    public double WorkingSetMB { get; set; }
    public double PrivateMemoryMB { get; set; }
    public double GcTotalMemoryMB { get; set; }
    public double CpuUsagePercent { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
}

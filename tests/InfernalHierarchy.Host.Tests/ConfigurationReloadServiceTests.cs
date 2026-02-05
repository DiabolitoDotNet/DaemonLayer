using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Host;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class ConfigurationReloadServiceTests
{
    private sealed class ListLogger<T> : ILogger<T>
    {
        public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

        private readonly List<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => _entries;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class ThrowingOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public T CurrentValue => throw new InvalidOperationException("boom");

        public T Get(string? name) => throw new InvalidOperationException("boom");

        public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class TestableConfigurationReloadService : ConfigurationReloadService
    {
        public TestableConfigurationReloadService(
            ILogger<ConfigurationReloadService> logger,
            IConfiguration configuration,
            IOptionsMonitor<HierarchyOptions> hierarchyOptions,
            IOptionsMonitor<MemoryOptions> memoryOptions,
            IOptionsMonitor<SearXNGOptions> searxngOptions)
            : base(
                logger,
                configuration,
                hierarchyOptions,
                memoryOptions,
                searxngOptions)
        {
        }

        public Task RunOnceAsync(CancellationToken token) => ExecuteAsync(token);
    }

    [Fact]
    public static async Task StartStopAsync_RegistersOptionChangeHandlers_AndDisposesOnStopAsync()
    {
        var logger = new ListLogger<ConfigurationReloadService>();
        var hierarchyMonitor = new TestOptionsMonitor<HierarchyOptions>(new HierarchyOptions
        {
            MainAgentName = "Lucifer",
            MainAgentPersonaPath = "souls/lucifer.json",
            MaxAgentDepth = 4
        });

        var memoryMonitor = new TestOptionsMonitor<MemoryOptions>(new MemoryOptions { DatabasePath = "memory.db" });
        var searxngMonitor = new TestOptionsMonitor<SearXNGOptions>(new SearXNGOptions { Enabled = false });

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        using var service = new TestableConfigurationReloadService(
            logger,
            config,
            hierarchyMonitor,
            memoryMonitor,
            searxngMonitor);

        await service.RunOnceAsync(CancellationToken.None);

        hierarchyMonitor.ListenerCount.Should().Be(1);
        memoryMonitor.ListenerCount.Should().Be(1);
        searxngMonitor.ListenerCount.Should().Be(1);

        await service.StopAsync(CancellationToken.None);

        hierarchyMonitor.ListenerCount.Should().Be(0);
        memoryMonitor.ListenerCount.Should().Be(0);
        searxngMonitor.ListenerCount.Should().Be(0);
    }

    [Fact]
    public static async Task OptionChanges_ShouldLogUpdatedValues()
    {
        var logger = new ListLogger<ConfigurationReloadService>();
        var hierarchyMonitor = new TestOptionsMonitor<HierarchyOptions>(new HierarchyOptions
        {
            MainAgentName = "Lucifer",
            MainAgentPersonaPath = "souls/lucifer.json",
            MaxAgentDepth = 4
        });

        var memoryMonitor = new TestOptionsMonitor<MemoryOptions>(new MemoryOptions { DatabasePath = "memory.db" });
        var searxngMonitor = new TestOptionsMonitor<SearXNGOptions>(new SearXNGOptions { Enabled = false, BaseUrl = new Uri("http://localhost:8080") });

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        using var service = new TestableConfigurationReloadService(
            logger,
            config,
            hierarchyMonitor,
            memoryMonitor,
            searxngMonitor);

        await service.RunOnceAsync(CancellationToken.None);

        hierarchyMonitor.Set(new HierarchyOptions { MainAgentName = "Baal", MaxAgentDepth = 9, MainAgentPersonaPath = "souls/baal.json" });
        memoryMonitor.Set(new MemoryOptions { DatabasePath = "new.db" });
        searxngMonitor.Set(new SearXNGOptions { Enabled = true, BaseUrl = new Uri("http://searxng") });

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("Hierarchy configuration changed", StringComparison.Ordinal));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("MainAgentName: Baal", StringComparison.Ordinal));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("MaxAgentDepth: 9", StringComparison.Ordinal));

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("Memory configuration changed", StringComparison.Ordinal));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("DatabasePath: new.db", StringComparison.Ordinal));

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("SearXNG configuration changed", StringComparison.Ordinal));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("BaseUrl: http://searxng", StringComparison.Ordinal));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("Enabled: True", StringComparison.Ordinal));
    }

    [Fact]
    public static async Task ConfigurationReload_ShouldIncrementReloadCount_AndLogSummary()
    {
        var logger = new ListLogger<ConfigurationReloadService>();
        var hierarchyMonitor = new TestOptionsMonitor<HierarchyOptions>(new HierarchyOptions { MainAgentName = "Lucifer", MaxAgentDepth = 4 });
        var memoryMonitor = new TestOptionsMonitor<MemoryOptions>(new MemoryOptions { DatabasePath = "memory.db" });
        var searxngMonitor = new TestOptionsMonitor<SearXNGOptions>(new SearXNGOptions { Enabled = true, BaseUrl = new Uri("http://searxng") });

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var configRoot = (IConfigurationRoot)config;

        using var service = new TestableConfigurationReloadService(
            logger,
            configRoot,
            hierarchyMonitor,
            memoryMonitor,
            searxngMonitor);

        await service.RunOnceAsync(CancellationToken.None);

        configRoot.Reload();

        await WaitUntilAsync(() => service.ReloadCount >= 1, TimeSpan.FromSeconds(2));

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("Configuration file reloaded", StringComparison.Ordinal));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("Current Configuration Summary", StringComparison.Ordinal));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("MainAgent", StringComparison.Ordinal));
    }

    [Fact]
    public static async Task ConfigurationReload_WhenSummaryThrows_ShouldLogError()
    {
        var logger = new ListLogger<ConfigurationReloadService>();
        var configRoot = new ConfigurationBuilder().AddInMemoryCollection().Build();

        using var service = new TestableConfigurationReloadService(
            logger,
            configRoot,
            new ThrowingOptionsMonitor<HierarchyOptions>(),
            new ThrowingOptionsMonitor<MemoryOptions>(),
            new ThrowingOptionsMonitor<SearXNGOptions>());

        await service.RunOnceAsync(CancellationToken.None);

        configRoot.Reload();

        await WaitUntilAsync(() => logger.Entries.Any(e => e.Level == LogLevel.Error), TimeSpan.FromSeconds(2));

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;

        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition was not met within timeout");
            }

            await Task.Delay(20);
        }
    }
}

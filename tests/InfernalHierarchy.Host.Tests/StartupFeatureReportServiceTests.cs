using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Memory.Configuration;
using InfernalHierarchy.Tools.Marketplace;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class StartupFeatureReportServiceTests
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

    [Fact]
    public async Task StartAsync_LogsEffectiveFeatureFlags()
    {
        var logger = new ListLogger<StartupFeatureReportService>();
        var service = new StartupFeatureReportService(
            Options.Create(new HttpEndpointOptions { Enabled = true, Urls = "http://localhost:5080" }),
            Options.Create(new UiInterfaceOptions { Enabled = true }),
            Options.Create(new WebSocketInterfaceOptions { Enabled = false }),
            Options.Create(new VoiceInterfaceOptions { Enabled = true }),
            Options.Create(new VoiceCopilotOptions { Enabled = false }),
            Options.Create(new VectorMemoryOptions { Enabled = true }),
            Options.Create(new MemoryPruningOptions { Enabled = false }),
            Options.Create(new MemoryLearningOptions { Enabled = true }),
            Options.Create(new ToolMarketplaceOptions { Enabled = false }),
            Options.Create(new ToolResultCacheOptions { Enabled = true, ClearOnStartup = true }),
            Options.Create(new OpenTelemetryExportOptions
            {
                Console = new ConsoleExporterOptions { Enabled = true },
                Otlp = new OtlpExporterOptions { Enabled = false }
            }),
            logger);

        await service.StartAsync(CancellationToken.None);

        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Startup features", StringComparison.Ordinal)
            && entry.Message.Contains("http=True", StringComparison.Ordinal)
            && entry.Message.Contains("urls=http://localhost:5080", StringComparison.Ordinal)
            && entry.Message.Contains("voice=True", StringComparison.Ordinal)
            && entry.Message.Contains("voice_copilot=False", StringComparison.Ordinal)
            && entry.Message.Contains("tool_cache=True", StringComparison.Ordinal)
            && entry.Message.Contains("tool_cache_clear_on_startup=True", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopAsync_CompletesWithoutWork()
    {
        var service = new StartupFeatureReportService(
            Options.Create(new HttpEndpointOptions()),
            Options.Create(new UiInterfaceOptions()),
            Options.Create(new WebSocketInterfaceOptions()),
            Options.Create(new VoiceInterfaceOptions()),
            Options.Create(new VoiceCopilotOptions()),
            Options.Create(new VectorMemoryOptions()),
            Options.Create(new MemoryPruningOptions()),
            Options.Create(new MemoryLearningOptions()),
            Options.Create(new ToolMarketplaceOptions()),
            Options.Create(new ToolResultCacheOptions()),
            Options.Create(new OpenTelemetryExportOptions()),
            new ListLogger<StartupFeatureReportService>());

        await service.StopAsync(CancellationToken.None);

        true.Should().BeTrue();
    }
}
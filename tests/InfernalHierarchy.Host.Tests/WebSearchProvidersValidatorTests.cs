using InfernalHierarchy.Host.Configuration.Validation;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class WebSearchProvidersValidatorTests
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
            public void Dispose() { }
        }
    }

    [Fact]
    public void Validate_SearXng_WhenBothProvidersDisabled_LogsWarningAndReturnsSuccess()
    {
        var searx = new TestOptionsMonitor<SearXNGOptions>(new SearXNGOptions { Enabled = false });
        var brave = new TestOptionsMonitor<BraveSearchOptions>(new BraveSearchOptions { Enabled = false });
        var logger = new ListLogger<WebSearchProvidersValidator>();

        var validator = new WebSearchProvidersValidator(searx, brave, logger);

        var result = validator.Validate(name: null, new SearXNGOptions { Enabled = false });

        Assert.True(result.Succeeded);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Both SearXNG and Brave Search are disabled", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Brave_WhenAtLeastOneProviderEnabled_DoesNotLogWarningAndReturnsSuccess()
    {
        var searx = new TestOptionsMonitor<SearXNGOptions>(new SearXNGOptions { Enabled = true });
        var brave = new TestOptionsMonitor<BraveSearchOptions>(new BraveSearchOptions { Enabled = false });
        var logger = new ListLogger<WebSearchProvidersValidator>();

        var validator = new WebSearchProvidersValidator(searx, brave, logger);

        var result = validator.Validate(name: null, new BraveSearchOptions { Enabled = false });

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Both SearXNG and Brave Search are disabled", StringComparison.Ordinal));
    }
}

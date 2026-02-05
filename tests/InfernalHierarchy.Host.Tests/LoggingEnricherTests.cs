using FluentAssertions;
using InfernalHierarchy.Host.Observability;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class LoggingEnricherTests
{
    [Fact]
    public void LoggingEnricher_AddsStandardProperties()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");

        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new LoggingEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("hello");

        var ev = sink.SingleEvent;
        ev.Properties.Should().ContainKey("Application");
        ev.Properties["Application"].Should().BeOfType<ScalarValue>().Which.Value.Should().Be("InfernalHierarchy");

        ev.Properties.Should().ContainKey("Environment");
        ev.Properties["Environment"].Should().BeOfType<ScalarValue>().Which.Value.Should().Be("Test");

        ev.Properties.Should().ContainKey("ProcessId");
        ev.Properties["ProcessId"].Should().BeOfType<ScalarValue>().Which.Value.Should().Be(Environment.ProcessId);
    }

    [Fact]
    public void ContextEnrichers_AddOnlyWhenSet()
    {
        var sink = new CollectingSink();
        var agent = new AgentContextEnricher
        {
            AgentId = "a1",
            AgentName = "Lucifer",
            AgentRank = "Supreme",
        };

        var message = new MessageContextEnricher
        {
            MessageId = "m1",
            MessageType = "Command",
            CorrelationId = "c1",
        };

        var tool = new ToolContextEnricher
        {
            ToolName = "web_search",
            ToolExecutionId = "t1",
        };

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(agent)
            .Enrich.With(message)
            .Enrich.With(tool)
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("hello");

        var ev = sink.SingleEvent;
        ev.Properties.Should().ContainKey("AgentId");
        ev.Properties.Should().ContainKey("AgentName");
        ev.Properties.Should().ContainKey("AgentRank");
        ev.Properties.Should().ContainKey("MessageId");
        ev.Properties.Should().ContainKey("MessageType");
        ev.Properties.Should().ContainKey("CorrelationId");
        ev.Properties.Should().ContainKey("ToolName");
        ev.Properties.Should().ContainKey("ToolExecutionId");

        // when unset, should not add
        sink.Clear();
        agent.AgentId = null;
        agent.AgentName = string.Empty;
        agent.AgentRank = null;
        message.MessageId = null;
        message.MessageType = null;
        message.CorrelationId = null;
        tool.ToolName = null;
        tool.ToolExecutionId = null;

        logger.Information("hello2");

        var ev2 = sink.SingleEvent;
        ev2.Properties.Should().NotContainKey("AgentId");
        ev2.Properties.Should().NotContainKey("AgentName");
        ev2.Properties.Should().NotContainKey("AgentRank");
        ev2.Properties.Should().NotContainKey("MessageId");
        ev2.Properties.Should().NotContainKey("MessageType");
        ev2.Properties.Should().NotContainKey("CorrelationId");
        ev2.Properties.Should().NotContainKey("ToolName");
        ev2.Properties.Should().NotContainKey("ToolExecutionId");
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = new();

        public LogEvent SingleEvent => _events.Single();

        public void Emit(LogEvent logEvent) => _events.Add(logEvent);

        public void Clear() => _events.Clear();
    }
}

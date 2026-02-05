using System.Diagnostics;
using FluentAssertions;
using InfernalHierarchy.Host.Observability;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class DistributedTracingUnitTests
{
    private static IDisposable EnableActivityRecording()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "InfernalHierarchy",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void StartAgentActivity_SetsExpectedTags()
    {
        using var _ = EnableActivityRecording();

        var logger = new Mock<ILogger<DistributedTracing>>();
        var messageEnricher = new MessageContextEnricher();
        var tracing = new DistributedTracing(logger.Object, messageEnricher);

        using var activity = tracing.StartAgentActivity("Lucifer", "agent-1", "Think");

        activity.Should().NotBeNull();
        activity!.DisplayName.Should().Be("Agent.Think");
        activity.GetTagItem("agent.name").Should().Be("Lucifer");
        activity.GetTagItem("agent.id").Should().Be("agent-1");
        activity.GetTagItem("operation.type").Should().Be("Think");
    }

    [Fact]
    public void StartMessageActivity_SetsTags_AndEnricherContext()
    {
        using var _ = EnableActivityRecording();

        var logger = new Mock<ILogger<DistributedTracing>>();
        var messageEnricher = new MessageContextEnricher();
        var tracing = new DistributedTracing(logger.Object, messageEnricher);

        using var activity = tracing.StartMessageActivity("msg-1", "Command", "from-1", null);

        activity.Should().NotBeNull();
        activity!.GetTagItem("message.id").Should().Be("msg-1");
        activity.GetTagItem("message.type").Should().Be("Command");
        activity.GetTagItem("message.from").Should().Be("from-1");
        activity.GetTagItem("message.to").Should().Be("broadcast");

        messageEnricher.MessageId.Should().Be("msg-1");
        messageEnricher.MessageType.Should().Be("Command");
        messageEnricher.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RecordError_SetsActivityErrorStatus_AndLogs()
    {
        using var _ = EnableActivityRecording();

        var logger = new Mock<ILogger<DistributedTracing>>();
        var messageEnricher = new MessageContextEnricher();
        var tracing = new DistributedTracing(logger.Object, messageEnricher);

        using var activity = tracing.StartToolActivity("web_search", "agent-1");
        var exception = new InvalidOperationException("boom");

        tracing.RecordError(activity, exception);

        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("boom");
        activity.GetTagItem("error.type").Should().Be("InvalidOperationException");
        activity.GetTagItem("error.message").Should().Be("boom");

        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Error recorded in activity")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void AddEvent_AddsActivityEventWithTags()
    {
        using var _ = EnableActivityRecording();

        var logger = new Mock<ILogger<DistributedTracing>>();
        var messageEnricher = new MessageContextEnricher();
        var tracing = new DistributedTracing(logger.Object, messageEnricher);

        using var activity = tracing.StartMemoryActivity("Read", "Fact");

        tracing.AddEvent(activity, "memory.read", new Dictionary<string, object?>
        {
            ["tenant"] = "t1",
            ["count"] = 2,
        });

        var ev = activity!.Events.Single(e => e.Name == "memory.read");
        ev.Tags.Should().Contain(t => t.Key == "tenant" && (string?)t.Value == "t1");
        ev.Tags.Should().Contain(t => t.Key == "count" && (int?)t.Value == 2);
    }

    [Fact]
    public void ActivityExtensions_SetStatusAndTags()
    {
        using var _ = EnableActivityRecording();

        var logger = new Mock<ILogger<DistributedTracing>>();
        var messageEnricher = new MessageContextEnricher();
        var tracing = new DistributedTracing(logger.Object, messageEnricher);

        using var activity = tracing.StartLlmActivity("llama3", "agent-1");

        activity.RecordMetric("tokens", 123);
        activity.RecordDuration("prompt", TimeSpan.FromMilliseconds(45));
        activity.RecordSuccess();

        activity!.Status.Should().Be(ActivityStatusCode.Ok);
        activity.GetTagItem("metric.tokens").Should().Be(123d);
        activity.GetTagItem("duration.prompt.ms").Should().BeOfType<double>().Which.Should().BeApproximately(45d, 0.001);
    }

    [Fact]
    public void ActivityScope_DelegatesToTracingAndDisposesActivity()
    {
        using var _ = EnableActivityRecording();

        var logger = new Mock<ILogger<DistributedTracing>>();
        var messageEnricher = new MessageContextEnricher();
        var tracing = new DistributedTracing(logger.Object, messageEnricher);

        var activity = tracing.StartAgentActivity("Baal", "agent-2", "Act");
        using var scope = new ActivityScope(activity, tracing);

        scope.AddTag("k", "v");
        scope.RecordSuccess();

        scope.Activity!.GetTagItem("k").Should().Be("v");
        scope.Activity.Status.Should().Be(ActivityStatusCode.Ok);
    }
}

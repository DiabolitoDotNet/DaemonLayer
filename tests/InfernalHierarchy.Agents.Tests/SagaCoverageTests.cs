using FluentAssertions;
using InfernalHierarchy.Agents.Saga;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Core.Saga;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class SagaCoverageTests
{
    private sealed class TestSaga : SagaBase
    {
        public override string Name => "TestSaga";

        public TestSaga() : base(NullLogger.Instance)
        {
        }

        public void Add(ISagaStep step) => AddStep(step);
    }

    private sealed class TrackingStep : ISagaStep
    {
        private readonly List<string> _events;
        private readonly bool _throwOnExecute;
        private readonly bool _throwOnCompensate;

        public TrackingStep(string name, List<string> events, bool throwOnExecute = false, bool throwOnCompensate = false)
        {
            Name = name;
            _events = events;
            _throwOnExecute = throwOnExecute;
            _throwOnCompensate = throwOnCompensate;
        }

        public string Name { get; }

        public Task ExecuteAsync(SagaContext context, CancellationToken ct = default)
        {
            _events.Add($"exec:{Name}");
            if (_throwOnExecute)
            {
                throw new InvalidOperationException($"fail:{Name}");
            }

            return Task.CompletedTask;
        }

        public Task CompensateAsync(SagaContext context, CancellationToken ct = default)
        {
            _events.Add($"comp:{Name}");
            if (_throwOnCompensate)
            {
                throw new InvalidOperationException($"compfail:{Name}");
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SagaBase_WhenAllStepsSucceed_ReturnsSuccessAndNoCompensation()
    {
        var events = new List<string>();
        var saga = new TestSaga();
        saga.Add(new TrackingStep("a", events));
        saga.Add(new TrackingStep("b", events));

        var result = await saga.ExecuteAsync(CancellationToken.None);

        result.Success.Should().BeTrue();
        result.CompensationSuccess.Should().BeNull();
        result.Context.CompletedSteps.Should().BeEquivalentTo(["a", "b"], o => o.WithStrictOrdering());
        events.Should().BeEquivalentTo(["exec:a", "exec:b"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task SagaBase_WhenStepFails_CompensatesCompletedStepsInReverseOrder()
    {
        var events = new List<string>();
        var saga = new TestSaga();
        saga.Add(new TrackingStep("a", events));
        saga.Add(new TrackingStep("b", events, throwOnExecute: true));

        var result = await saga.ExecuteAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        result.CompensationSuccess.Should().BeTrue();
        result.FailureReasonCode.Should().Be("execution_step_failed");
        result.NextAction.Should().Be("saga_compensated");
        result.NeedsSupervisorIntervention.Should().BeFalse();
        result.Context.CompletedSteps.Should().BeEquivalentTo(["a"], o => o.WithStrictOrdering());
        result.Context.CompensatedSteps.Should().BeEquivalentTo(["a"], o => o.WithStrictOrdering());
        events.Should().BeEquivalentTo(["exec:a", "exec:b", "comp:a"], o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task SagaBase_WhenCompensationFails_SetsCompensationSuccessFalse()
    {
        var events = new List<string>();
        var saga = new TestSaga();
        saga.Add(new TrackingStep("a", events, throwOnCompensate: true));
        saga.Add(new TrackingStep("b", events, throwOnExecute: true));

        var result = await saga.ExecuteAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        result.CompensationSuccess.Should().BeFalse();
        result.FailureReasonCode.Should().Be("compensation_retry_exhausted");
        result.NextAction.Should().Be("request_supervisor_compensation_assistance");
        result.NeedsSupervisorIntervention.Should().BeTrue();
        result.Context.Data["CompensationFailureReasonCode"].Should().Be("compensation_retry_exhausted");
        result.Context.Data["SupervisorEscalationRequested"].Should().Be(true);
        events.Count(e => e == "comp:a").Should().Be(3);
    }

    [Fact]
    public void CreateCollaborationSaga_ConstructsExpectedSteps()
    {
        var logger = NullLogger<CreateCollaborationSaga>.Instance;
        var factory = Mock.Of<IAgentFactory>();
        var memory = Mock.Of<ISharedMemory>();
        var collab = Mock.Of<IAgentCollaborationService>();
        var request = new CollaborationRequest
        {
            Id = "r1",
            InitiatorAgentId = "lucifer",
            Task = "t",
            ParticipantAgentIds = ["a", "b", "c"],
            MinimumParticipants = 2
        };

        var saga = new CreateCollaborationSaga(logger, factory, memory, collab, request);

        saga.Name.Should().Be("CreateCollaboration");
        saga.Steps.Should().HaveCount(5);
        saga.Steps.Select(s => s.Name).Should().BeEquivalentTo(
            ["ValidateParticipants", "StoreCollaboration", "SendRequests", "AggregateResponses", "StoreFinalResult"],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task ValidateParticipantsStep_WhenInsufficientParticipants_Throws()
    {
        var step = new ValidateParticipantsStep(NullLogger.Instance, Mock.Of<IAgentFactory>());
        var context = new SagaContext
        {
            Data =
            {
                ["CollaborationRequest"] = new CollaborationRequest
                {
                    Id = "r1",
                    InitiatorAgentId = "lucifer",
                    Task = "t",
                    ParticipantAgentIds = ["a"],
                    MinimumParticipants = 2
                }
            }
        };

        var act = () => step.ExecuteAsync(context, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidateParticipantsStep_WhenEnoughParticipants_SetsValidatedParticipants()
    {
        var step = new ValidateParticipantsStep(NullLogger.Instance, Mock.Of<IAgentFactory>());
        var ids = new List<string> { "a", "b" };
        var context = new SagaContext
        {
            Data =
            {
                ["CollaborationRequest"] = new CollaborationRequest
                {
                    Id = "r1",
                    InitiatorAgentId = "lucifer",
                    Task = "t",
                    ParticipantAgentIds = ids,
                    MinimumParticipants = 2
                }
            }
        };

        await step.ExecuteAsync(context, CancellationToken.None);

        context.Data.Should().ContainKey("ValidatedParticipants");
        ((List<string>)context.Data["ValidatedParticipants"]).Should().BeEquivalentTo(ids);

        await step.CompensateAsync(context, CancellationToken.None);
    }

    [Fact]
    public async Task StoreCollaborationStep_ExecuteAndCompensate_UsesMemory()
    {
        var memory = new Mock<ISharedMemory>();
        memory
            .Setup(m => m.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        memory
            .Setup(m => m.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var step = new StoreCollaborationStep(NullLogger.Instance, memory.Object);
        var context = new SagaContext
        {
            Data =
            {
                ["CollaborationRequest"] = new CollaborationRequest
                {
                    Id = "r1",
                    InitiatorAgentId = "lucifer",
                    Task = "t",
                    ParticipantAgentIds = ["a", "b"],
                    MinimumParticipants = 2,
                    MinimumConfidence = 0.7,
                    Strategy = CollaborationStrategy.Voting
                }
            }
        };

        await step.ExecuteAsync(context, CancellationToken.None);
        context.Data.Should().ContainKey("DecisionId");

        await step.CompensateAsync(context, CancellationToken.None);
        memory.Verify(m => m.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendCollaborationRequestsStep_ExecuteAndCompensate_SetsFlag()
    {
        var step = new SendCollaborationRequestsStep(NullLogger.Instance, Mock.Of<IAgentCollaborationService>());
        var context = new SagaContext();

        await step.ExecuteAsync(context, CancellationToken.None);
        context.Data.Should().ContainKey("RequestsSent");

        await step.CompensateAsync(context, CancellationToken.None);
    }

    [Fact]
    public async Task AggregateResponsesStep_ExecuteAndCompensate_SetsAndClearsResult()
    {
        var step = new AggregateResponsesStep(NullLogger.Instance, Mock.Of<IAgentCollaborationService>());
        var context = new SagaContext();

        await step.ExecuteAsync(context, CancellationToken.None);
        context.Data.Should().ContainKey("AggregatedResult");

        await step.CompensateAsync(context, CancellationToken.None);
        context.Data.Should().NotContainKey("AggregatedResult");
    }

    [Fact]
    public async Task StoreFinalResultStep_ExecuteAndCompensate_UsesMemory()
    {
        var memory = new Mock<ISharedMemory>();
        memory
            .Setup(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        memory
            .Setup(m => m.DeleteFactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var step = new StoreFinalResultStep(NullLogger.Instance, memory.Object);
        var context = new SagaContext
        {
            Data =
            {
                ["AggregatedResult"] = new CollaborationResult
                {
                    Decision = "d",
                    Confidence = 0.9,
                    ParticipantCount = 1,
                    AggregatedReasoning = "r"
                }
            }
        };

        await step.ExecuteAsync(context, CancellationToken.None);
        context.Data.Should().ContainKey("ResultFactId");

        await step.CompensateAsync(context, CancellationToken.None);
        memory.Verify(m => m.DeleteFactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

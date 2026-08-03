using FluentAssertions;
using InfernalHierarchy.Agents.Saga;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class CreateCollaborationSagaExecutionTests
{
    [Fact]
    public async Task ExecuteAsync_WhenAllStepsSucceed_WritesDecisionAndFact()
    {
        var memory = new Mock<ISharedMemory>();
        memory
            .Setup(m => m.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        memory
            .Setup(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CollaborationRequest
        {
            Id = "r1",
            InitiatorAgentId = "lucifer",
            Task = "t",
            ParticipantAgentIds = ["a", "b", "c"],
            MinimumParticipants = 2,
            MinimumConfidence = 0.7,
            Strategy = CollaborationStrategy.Voting
        };

        var collaborationResult = new CollaborationResult
        {
            Decision = "A",
            Confidence = 0.91,
            ParticipantCount = 3,
            AgreementScore = 0.67,
            AggregatedReasoning = "majority reached",
            Strategy = CollaborationStrategy.Voting
        };

        var collaborationService = new Mock<IAgentCollaborationService>();
        collaborationService
            .Setup(s => s.RequestCollaborationAsync(It.IsAny<CollaborationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collaborationResult);

        var saga = new CreateCollaborationSaga(
            NullLogger<CreateCollaborationSaga>.Instance,
            Mock.Of<IAgentFactory>(),
            memory.Object,
            collaborationService.Object,
            request);

        var result = await saga.ExecuteAsync(CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Context.CompletedSteps.Should().HaveCount(5);

        memory.Verify(m => m.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()), Times.Once);
        memory.Verify(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()), Times.Once);
        memory.Verify(m => m.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        memory.Verify(m => m.DeleteFactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        collaborationService.Verify(
            s => s.RequestCollaborationAsync(
                It.Is<CollaborationRequest>(r => r.Id == request.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFinalStepFails_CompensatesAndDeletesDecision()
    {
        var memory = new Mock<ISharedMemory>();
        memory
            .Setup(m => m.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        memory
            .Setup(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("fact-write-failed"));
        memory
            .Setup(m => m.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new CollaborationRequest
        {
            Id = "r1",
            InitiatorAgentId = "lucifer",
            Task = "t",
            ParticipantAgentIds = ["a", "b", "c"],
            MinimumParticipants = 2,
            MinimumConfidence = 0.7,
            Strategy = CollaborationStrategy.Voting
        };

        var collaborationService = new Mock<IAgentCollaborationService>();
        collaborationService
            .Setup(s => s.RequestCollaborationAsync(It.IsAny<CollaborationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CollaborationResult
            {
                Decision = "B",
                Confidence = 0.88,
                ParticipantCount = 3,
                AgreementScore = 0.66,
                AggregatedReasoning = "weighted consensus",
                Strategy = CollaborationStrategy.WeightedVoting
            });
        collaborationService
            .Setup(s => s.CancelCollaborationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var saga = new CreateCollaborationSaga(
            NullLogger<CreateCollaborationSaga>.Instance,
            Mock.Of<IAgentFactory>(),
            memory.Object,
            collaborationService.Object,
            request);

        var result = await saga.ExecuteAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        result.CompensationSuccess.Should().BeTrue();
        result.FailureReasonCode.Should().Be("execution_step_failed");
        result.NeedsSupervisorIntervention.Should().BeFalse();

        memory.Verify(m => m.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()), Times.Once);
        memory.Verify(m => m.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        collaborationService.Verify(s => s.CancelCollaborationAsync(request.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenParticipantValidationFails_DoesNotPerformMemorySideEffects()
    {
        var memory = new Mock<ISharedMemory>(MockBehavior.Strict);
        var collaborationService = new Mock<IAgentCollaborationService>(MockBehavior.Strict);

        var request = new CollaborationRequest
        {
            Id = "r1",
            InitiatorAgentId = "lucifer",
            Task = "t",
            ParticipantAgentIds = ["a"],
            MinimumParticipants = 2,
            MinimumConfidence = 0.7,
            Strategy = CollaborationStrategy.Voting
        };

        var saga = new CreateCollaborationSaga(
            NullLogger<CreateCollaborationSaga>.Instance,
            Mock.Of<IAgentFactory>(),
            memory.Object,
            collaborationService.Object,
            request);

        var result = await saga.ExecuteAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        result.CompensationSuccess.Should().BeTrue();
        result.FailureReasonCode.Should().Be("execution_step_failed");
        memory.Verify(m => m.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()), Times.Never);
        memory.Verify(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()), Times.Never);
        memory.Verify(m => m.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        memory.Verify(m => m.DeleteFactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        collaborationService.Verify(s => s.RequestCollaborationAsync(It.IsAny<CollaborationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

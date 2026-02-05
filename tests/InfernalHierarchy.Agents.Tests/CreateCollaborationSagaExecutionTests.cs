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

        var saga = new CreateCollaborationSaga(
            NullLogger<CreateCollaborationSaga>.Instance,
            Mock.Of<IAgentFactory>(),
            memory.Object,
            Mock.Of<IAgentCollaborationService>(),
            request);

        var result = await saga.ExecuteAsync(CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Context.CompletedSteps.Should().HaveCount(5);

        memory.Verify(m => m.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()), Times.Once);
        memory.Verify(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()), Times.Once);
        memory.Verify(m => m.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        memory.Verify(m => m.DeleteFactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

        var saga = new CreateCollaborationSaga(
            NullLogger<CreateCollaborationSaga>.Instance,
            Mock.Of<IAgentFactory>(),
            memory.Object,
            Mock.Of<IAgentCollaborationService>(),
            request);

        var result = await saga.ExecuteAsync(CancellationToken.None);

        result.Success.Should().BeFalse();
        result.CompensationSuccess.Should().BeTrue();

        memory.Verify(m => m.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()), Times.Once);
        memory.Verify(m => m.DeleteDecisionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

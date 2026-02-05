using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public class AgentCollaborationServiceTests
{
    private static Mock<IAgent> CreateAgent(string id, AgentRank rank)
    {
        var persona = new Persona { Name = $"{rank}Agent" };

        var mock = new Mock<IAgent>();
        mock.SetupGet(a => a.Id).Returns(id);
        mock.SetupGet(a => a.Name).Returns(id);
        mock.SetupGet(a => a.Rank).Returns(rank);
        mock.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        mock.SetupGet(a => a.Persona).Returns(persona);
        mock.Setup(a => a.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(a => a.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(a => a.SuspendAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(a => a.ResumeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(a => a.ProcessTaskAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentMessage m, CancellationToken _) => m);
        mock.Setup(a => a.CanCreateSubAgent(It.IsAny<AgentRank>())).Returns(false);
        return mock;
    }

    [Fact]
    public async Task RequestCollaborationAsync_Voting_SelectsMajorityDecision()
    {
        // Arrange
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        registry.Register(CreateAgent("a1", AgentRank.Worker).Object);
        registry.Register(CreateAgent("a2", AgentRank.Worker).Object);
        registry.Register(CreateAgent("a3", AgentRank.Worker).Object);

        var service = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus.Object,
            registry);

        var request = new CollaborationRequest
        {
            Id = Guid.NewGuid().ToString(),
            InitiatorAgentId = "init",
            Task = "Choose A or B",
            Strategy = CollaborationStrategy.Voting,
            MinimumParticipants = 3,
            MinimumConfidence = 0.6,
            Timeout = TimeSpan.FromSeconds(2),
            ParticipantAgentIds = new List<string> { "a1", "a2", "a3" }
        };

        var collaborationTask = service.RequestCollaborationAsync(request, CancellationToken.None);

        // Act
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a1", AgentRank = AgentRank.Worker, Response = "A", Confidence = 0.7, Reasoning = "pref A" });
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a2", AgentRank = AgentRank.Worker, Response = "B", Confidence = 0.7, Reasoning = "pref B" });
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a3", AgentRank = AgentRank.Worker, Response = "B", Confidence = 0.7, Reasoning = "also B" });

        var result = await collaborationTask;

        // Assert
        result.Strategy.Should().Be(CollaborationStrategy.Voting);
        result.Decision.Should().Be("B");
        result.ParticipantCount.Should().Be(3);
        result.AgreementScore.Should().BeApproximately(2.0 / 3.0, 0.0001);
    }

    [Fact]
    public async Task RequestCollaborationAsync_DefaultVotingStrategy_DynamicallySelectsHierarchical_WhenMixedRanks()
    {
        // Arrange
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        registry.Register(CreateAgent("sup", AgentRank.Supreme).Object);
        registry.Register(CreateAgent("w1", AgentRank.Worker).Object);
        registry.Register(CreateAgent("w2", AgentRank.Worker).Object);

        var service = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus.Object,
            registry);

        var request = new CollaborationRequest
        {
            Id = Guid.NewGuid().ToString(),
            InitiatorAgentId = "init",
            Task = "Decide deployment approach",
            Strategy = CollaborationStrategy.Voting, // allows dynamic strategy selection
            MinimumParticipants = 3,
            MinimumConfidence = 0.6,
            Timeout = TimeSpan.FromSeconds(2),
            ParticipantAgentIds = new List<string> { "sup", "w1", "w2" }
        };

        var collaborationTask = service.RequestCollaborationAsync(request, CancellationToken.None);

        // Act
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "w1", AgentRank = AgentRank.Worker, Response = "OptionB", Confidence = 0.9, Reasoning = "fast" });
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "w2", AgentRank = AgentRank.Worker, Response = "OptionB", Confidence = 0.9, Reasoning = "cheap" });
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "sup", AgentRank = AgentRank.Supreme, Response = "OptionA", Confidence = 0.6, Reasoning = "safer" });

        var result = await collaborationTask;

        // Assert
        result.Strategy.Should().Be(CollaborationStrategy.Hierarchical);
        result.Decision.Should().Be("OptionA");
        result.WinningResponse.Should().NotBeNull();
        result.WinningResponse!.AgentRank.Should().Be(AgentRank.Supreme);
    }

    [Fact]
    public async Task RequestCollaborationAsync_ConsensusStrategy_PerformsMultiRoundRefinement_UntilConfidenceThresholdMet()
    {
        // Arrange
        var publishCount = 0;
        var firstRoundPublished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRoundPublished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback((AgentMessage msg, CancellationToken _) =>
            {
                // 2 participants => 2 publish calls per round
                var current = Interlocked.Increment(ref publishCount);
                if (current == 2)
                {
                    firstRoundPublished.TrySetResult(true);
                }
                if (current == 4)
                {
                    secondRoundPublished.TrySetResult(true);
                }
            });

        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        registry.Register(CreateAgent("a1", AgentRank.Duke).Object);
        registry.Register(CreateAgent("a2", AgentRank.Duke).Object);

        var service = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus.Object,
            registry);

        var request = new CollaborationRequest
        {
            Id = Guid.NewGuid().ToString(),
            InitiatorAgentId = "init",
            Task = "Pick one option",
            Strategy = CollaborationStrategy.Consensus,
            MinimumParticipants = 2,
            MinimumConfidence = 0.8,
            Timeout = TimeSpan.FromSeconds(2),
            ParticipantAgentIds = new List<string> { "a1", "a2" }
        };

        var collaborationTask = service.RequestCollaborationAsync(request, CancellationToken.None);

        // Wait until first round requests have been published
        await firstRoundPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Act - round 1 disagreement
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a1", AgentRank = AgentRank.Duke, Response = "A", Confidence = 0.9, Reasoning = "" });
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a2", AgentRank = AgentRank.Duke, Response = "B", Confidence = 0.9, Reasoning = "" });

        // Wait until round 2 is initiated
        await secondRoundPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Act - round 2 convergence
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a1", AgentRank = AgentRank.Duke, Response = "A", Confidence = 0.9, Reasoning = "updated" });
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a2", AgentRank = AgentRank.Duke, Response = "A", Confidence = 0.9, Reasoning = "updated" });

        var result = await collaborationTask;

        // Assert
        publishCount.Should().BeGreaterThanOrEqualTo(4);
        result.Strategy.Should().Be(CollaborationStrategy.Consensus);
        result.Decision.Should().Be("A");
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.8);
    }

    [Fact]
    public async Task RequestCollaborationAsync_ShouldReturnInsufficientParticipants_WhenBelowMinimum()
    {
        var bus = new Mock<IMessageBus>();
        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        registry.Register(CreateAgent("a1", AgentRank.Worker).Object);

        var service = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus.Object,
            registry);

        var request = new CollaborationRequest
        {
            Id = Guid.NewGuid().ToString(),
            InitiatorAgentId = "init",
            Task = "Do thing",
            Strategy = CollaborationStrategy.Voting,
            MinimumParticipants = 2,
            MinimumConfidence = 0.5,
            Timeout = TimeSpan.FromMilliseconds(50),
            ParticipantAgentIds = new List<string> { "a1" }
        };

        var result = await service.RequestCollaborationAsync(request, CancellationToken.None);

        result.Decision.Should().Be("INSUFFICIENT_PARTICIPANTS");
        result.Confidence.Should().Be(0.0);
        bus.Verify(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitResponseAsync_ShouldNotThrow_WhenRequestUnknown()
    {
        var bus = new Mock<IMessageBus>();
        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        var service = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus.Object,
            registry);

        var act = async () => await service.SubmitResponseAsync(
            "missing",
            new AgentResponse { AgentId = "a1", AgentRank = AgentRank.Worker, Response = "A", Confidence = 1.0 },
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SubmitResponseAsync_ShouldIgnoreStaleResponse_FromBeforeRoundStart()
    {
        var firstPublish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => firstPublish.TrySetResult(true));

        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        registry.Register(CreateAgent("a1", AgentRank.Worker).Object);

        var service = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus.Object,
            registry);

        var request = new CollaborationRequest
        {
            Id = Guid.NewGuid().ToString(),
            InitiatorAgentId = "init",
            Task = "Do something",
            Strategy = CollaborationStrategy.HighestConfidence,
            MinimumParticipants = 1,
            MinimumConfidence = 0.1,
            Timeout = TimeSpan.FromSeconds(2),
            ParticipantAgentIds = new List<string> { "a1" }
        };

        var collaborationTask = service.RequestCollaborationAsync(request, CancellationToken.None);

        await firstPublish.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.SubmitResponseAsync(request.Id, new AgentResponse
        {
            AgentId = "a1",
            AgentRank = AgentRank.Worker,
            Response = "STALE",
            Confidence = 1.0,
            Timestamp = DateTime.UtcNow.AddMinutes(-10)
        });

        await service.SubmitResponseAsync(request.Id, new AgentResponse
        {
            AgentId = "a1",
            AgentRank = AgentRank.Worker,
            Response = "FRESH",
            Confidence = 0.9,
            Reasoning = "new",
            Timestamp = DateTime.UtcNow
        });

        var result = await collaborationTask;
        result.Decision.Should().Be("FRESH");
        result.Responses.Should().HaveCount(1);
    }

    [Fact]
    public async Task RequestCollaborationAsync_WhenTimeoutWithPartialResponses_ShouldAggregatePartial_AndMarkTimedOut()
    {
        var firstPublish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => firstPublish.TrySetResult(true));

        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        registry.Register(CreateAgent("a1", AgentRank.Worker).Object);
        registry.Register(CreateAgent("a2", AgentRank.Worker).Object);

        var service = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus.Object,
            registry);

        var request = new CollaborationRequest
        {
            Id = Guid.NewGuid().ToString(),
            InitiatorAgentId = "init",
            Task = "Pick A or B",
            Strategy = CollaborationStrategy.HighestConfidence,
            MinimumParticipants = 2,
            MinimumConfidence = 0.5,
            Timeout = TimeSpan.FromMilliseconds(250),
            ParticipantAgentIds = new List<string> { "a1", "a2" }
        };

        var collaborationTask = service.RequestCollaborationAsync(request, CancellationToken.None);

        await firstPublish.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.SubmitResponseAsync(request.Id, new AgentResponse
        {
            AgentId = "a1",
            AgentRank = AgentRank.Worker,
            Response = "A",
            Confidence = 0.8,
            Reasoning = "partial"
        });

        var result = await collaborationTask;

        request.Status.Should().Be(CollaborationStatus.TimedOut);
        result.Decision.Should().Be("A");
        result.ParticipantCount.Should().Be(1);
    }

    [Fact]
    public async Task RequestCollaborationAsync_WhenCancelled_ShouldReturnCancelled_AndMarkRequestCancelled()
    {
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        registry.Register(CreateAgent("a1", AgentRank.Worker).Object);

        var service = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus.Object,
            registry);

        var request = new CollaborationRequest
        {
            Id = Guid.NewGuid().ToString(),
            InitiatorAgentId = "init",
            Task = "Do thing",
            Strategy = CollaborationStrategy.Voting,
            MinimumParticipants = 1,
            MinimumConfidence = 0.5,
            Timeout = TimeSpan.FromSeconds(2),
            ParticipantAgentIds = new List<string> { "a1" }
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.RequestCollaborationAsync(request, cts.Token);

        request.Status.Should().Be(CollaborationStatus.Cancelled);
        result.Decision.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task RequestCollaborationAsync_VotingWithLowAgreement_ShouldEscalateToWeightedVoting()
    {
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        registry.Register(CreateAgent("a1", AgentRank.Worker).Object);
        registry.Register(CreateAgent("a2", AgentRank.Worker).Object);
        registry.Register(CreateAgent("a3", AgentRank.Worker).Object);

        var service = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus.Object,
            registry);

        var request = new CollaborationRequest
        {
            Id = Guid.NewGuid().ToString(),
            InitiatorAgentId = "init",
            Task = "Choose A or B",
            Strategy = CollaborationStrategy.Voting,
            MinimumParticipants = 3,
            MinimumConfidence = 0.9,
            Timeout = TimeSpan.FromSeconds(2),
            ParticipantAgentIds = new List<string> { "a1", "a2", "a3" }
        };

        var collaborationTask = service.RequestCollaborationAsync(request, CancellationToken.None);

        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a1", AgentRank = AgentRank.Worker, Response = "A", Confidence = 1.0, Reasoning = "" });
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a2", AgentRank = AgentRank.Worker, Response = "B", Confidence = 1.0, Reasoning = "" });
        await service.SubmitResponseAsync(request.Id, new AgentResponse { AgentId = "a3", AgentRank = AgentRank.Worker, Response = "A", Confidence = 1.0, Reasoning = "" });

        var result = await collaborationTask;

        result.Strategy.Should().Be(CollaborationStrategy.WeightedVoting);
        result.Decision.Should().Be("A");
    }
}

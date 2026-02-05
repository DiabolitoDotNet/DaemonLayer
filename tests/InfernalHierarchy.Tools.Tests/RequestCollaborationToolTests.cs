using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public class RequestCollaborationToolTests
{
    private static readonly string[] ExpectedParticipantsAandB = new[] { "a", "b" };
    private static readonly string[] ExpectedRankedParticipants = new[] { "duke1", "w1", "w2" };

    private static IAgent CreateAgent(string id, AgentRank rank, AgentStatus status)
    {
        var mock = new Mock<IAgent>();
        mock.SetupGet(a => a.Id).Returns(id);
        mock.SetupGet(a => a.Name).Returns($"agent-{id}");
        mock.SetupGet(a => a.Rank).Returns(rank);
        mock.SetupGet(a => a.Status).Returns(status);
        mock.SetupGet(a => a.Persona).Returns(new Persona
        {
            Name = "Test",
            SystemPrompt = "Test",
            Specializations = new List<string>(),
            AvailableTools = new List<string>()
        });
        return mock.Object;
    }

    [Fact]
    public async Task ExecuteAsync_WhenTaskMissing_ShouldReturnError()
    {
        var logger = new Mock<ILogger<RequestCollaborationTool>>();
        var collaboration = new Mock<IAgentCollaborationService>();
        var registry = new Mock<IAgentRegistry>();

        var tool = new RequestCollaborationTool(logger.Object, collaboration.Object, registry.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("'task' parameter is required");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTaskEmpty_ShouldReturnError()
    {
        var logger = new Mock<ILogger<RequestCollaborationTool>>();
        var collaboration = new Mock<IAgentCollaborationService>();
        var registry = new Mock<IAgentRegistry>();

        var tool = new RequestCollaborationTool(logger.Object, collaboration.Object, registry.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "   "
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Task cannot be empty");
    }

    [Fact]
    public async Task ExecuteAsync_WhenNotEnoughAgents_ShouldReturnError()
    {
        var logger = new Mock<ILogger<RequestCollaborationTool>>();
        var collaboration = new Mock<IAgentCollaborationService>();
        var registry = new Mock<IAgentRegistry>();

        registry.Setup(r => r.GetAllAgents()).Returns(new[]
        {
            CreateAgent("a", AgentRank.Worker, AgentStatus.Idle)
        });

        var tool = new RequestCollaborationTool(logger.Object, collaboration.Object, registry.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Need consensus",
            ["min_participants"] = 2,
            ["agent_id"] = "initiator"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Not enough active agents available");
    }

    [Fact]
    public async Task ExecuteAsync_WithDefaultParticipants_ShouldCallCollaborationService()
    {
        var logger = new Mock<ILogger<RequestCollaborationTool>>();
        var collaboration = new Mock<IAgentCollaborationService>();
        var registry = new Mock<IAgentRegistry>();

        registry.Setup(r => r.GetAllAgents()).Returns(new[]
        {
            CreateAgent("initiator", AgentRank.Duke, AgentStatus.Idle),
            CreateAgent("a", AgentRank.Worker, AgentStatus.Idle),
            CreateAgent("b", AgentRank.Worker, AgentStatus.Thinking),
            CreateAgent("c", AgentRank.Worker, AgentStatus.Suspended)
        });

        CollaborationRequest? captured = null;
        collaboration
            .Setup(s => s.RequestCollaborationAsync(It.IsAny<CollaborationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CollaborationRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new CollaborationResult
            {
                Decision = "Do the thing",
                Confidence = 0.91,
                AgreementScore = 0.8,
                ParticipantCount = 2,
                Strategy = CollaborationStrategy.WeightedVoting,
                AggregatedReasoning = "Reasoning"
            });

        var tool = new RequestCollaborationTool(logger.Object, collaboration.Object, registry.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Need consensus",
            ["agent_id"] = "initiator"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().NotBeNull();
        result.Output!.Should().Contain("Collaboration completed");

        captured.Should().NotBeNull();
        captured!.InitiatorAgentId.Should().Be("initiator");
        captured.Task.Should().Be("Need consensus");
        captured.ParticipantAgentIds.Should().BeEquivalentTo(ExpectedParticipantsAandB);
        captured.MinimumParticipants.Should().Be(2);
        captured.MinimumConfidence.Should().Be(0.7);
        captured.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ExecuteAsync_WithParticipantRanks_ShouldUseRankedAgents()
    {
        var logger = new Mock<ILogger<RequestCollaborationTool>>();
        var collaboration = new Mock<IAgentCollaborationService>();
        var registry = new Mock<IAgentRegistry>();

        registry.Setup(r => r.GetAgentsByRank(AgentRank.Duke)).Returns(new[]
        {
            CreateAgent("duke1", AgentRank.Duke, AgentStatus.Idle)
        });
        registry.Setup(r => r.GetAgentsByRank(AgentRank.Worker)).Returns(new[]
        {
            CreateAgent("w1", AgentRank.Worker, AgentStatus.Idle),
            CreateAgent("w2", AgentRank.Worker, AgentStatus.Idle)
        });

        CollaborationRequest? captured = null;
        collaboration
            .Setup(s => s.RequestCollaborationAsync(It.IsAny<CollaborationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CollaborationRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new CollaborationResult
            {
                Decision = "Ok",
                Confidence = 0.8,
                AgreementScore = 0.7,
                ParticipantCount = 3,
                Strategy = CollaborationStrategy.Voting,
                AggregatedReasoning = "Reasoning"
            });

        var tool = new RequestCollaborationTool(logger.Object, collaboration.Object, registry.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["task"] = "Pick approach",
            ["strategy"] = "voting",
            ["participant_ranks"] = "duke,worker",
            ["min_participants"] = 2
        });

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.ParticipantAgentIds.Should().BeEquivalentTo(ExpectedRankedParticipants);
        captured.Strategy.Should().Be(CollaborationStrategy.Voting);
    }
}

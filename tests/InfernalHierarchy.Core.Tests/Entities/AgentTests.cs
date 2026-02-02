using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using Xunit;

namespace InfernalHierarchy.Core.Tests.Entities;

public class AgentTests
{
    [Fact]
    public void Agent_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var agent = new Agent
        {
            Name = "TestAgent",
            Rank = AgentRank.Duke
        };

        // Assert
        agent.Id.Should().NotBeNullOrEmpty();
        agent.Name.Should().Be("TestAgent");
        agent.Rank.Should().Be(AgentRank.Duke);
        agent.Status.Should().Be(AgentStatus.Idle);
        agent.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Agent_ShouldSupportHierarchy()
    {
        // Arrange
        var parentAgent = new Agent { Name = "Parent", Rank = AgentRank.Supreme };
        var childAgent = new Agent
        {
            Name = "Child",
            Rank = AgentRank.Prince,
            ParentAgentId = parentAgent.Id
        };

        // Assert
        childAgent.ParentAgentId.Should().Be(parentAgent.Id);
    }

    [Theory]
    [InlineData(AgentRank.Supreme)]
    [InlineData(AgentRank.Prince)]
    [InlineData(AgentRank.Duke)]
    [InlineData(AgentRank.Worker)]
    public void Agent_ShouldSupportAllRanks(AgentRank rank)
    {
        // Arrange & Act
        var agent = new Agent { Rank = rank };

        // Assert
        agent.Rank.Should().Be(rank);
    }

    [Fact]
    public void Agent_ShouldSupportMetadata()
    {
        // Arrange
        var agent = new Agent { Name = "MetadataTest" };

        // Act
        agent.Metadata["customKey"] = "customValue";
        agent.Metadata["taskCount"] = 42;

        // Assert
        agent.Metadata.Should().ContainKey("customKey");
        agent.Metadata["customKey"].Should().Be("customValue");
        agent.Metadata["taskCount"].Should().Be(42);
    }
}

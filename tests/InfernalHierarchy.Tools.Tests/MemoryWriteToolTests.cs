using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class MemoryWriteToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenTypeMissing()
    {
        var memory = new Mock<ISharedMemory>();
        var tool = new MemoryWriteTool(memory.Object, Mock.Of<ILogger<MemoryWriteTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["agent_id"] = "a1"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("type");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenAgentIdMissing()
    {
        var memory = new Mock<ISharedMemory>();
        var tool = new MemoryWriteTool(memory.Object, Mock.Of<ILogger<MemoryWriteTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["type"] = "decision"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("agent_id");
    }

    [Fact]
    public async Task ExecuteAsync_Decision_ShouldAddDecision()
    {
        var memory = new Mock<ISharedMemory>();
        memory.Setup(m => m.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = new MemoryWriteTool(memory.Object, Mock.Of<ILogger<MemoryWriteTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["type"] = "decision",
            ["agent_id"] = "a1",
            ["action"] = "do-x",
            ["context"] = "ctx",
            ["reasoning"] = "why"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Decision recorded");

        memory.Verify(m => m.AddDecisionAsync(
            It.Is<Decision>(d => d.CreatedBy == "a1" && d.Action == "do-x" && d.Context == "ctx" && d.Reasoning == "why"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Fact_WithVectorMemory_ShouldIndexFact_AndIncludeVisibilityInfo()
    {
        var memory = new Mock<ISharedMemory>();
        var vector = new Mock<IVectorMemory>();
        vector.Setup(v => v.IndexFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = new MemoryWriteTool(memory.Object, vector.Object, Mock.Of<ILogger<MemoryWriteTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["type"] = "fact",
            ["agent_id"] = "a1",
            ["category"] = "c1",
            ["content"] = "hello",
            ["visibility"] = "Public"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Fact recorded in category: c1");
        result.Output.Should().Contain("public");

        vector.Verify(v => v.IndexFactAsync(
            It.Is<Fact>(f => f.CreatedBy == "a1" && f.Category == "c1" && f.Content == "hello" && f.Visibility == MemoryVisibility.Public),
            It.IsAny<CancellationToken>()), Times.Once);

        memory.Verify(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Fact_SharedAndRankBased_ShouldParseFields()
    {
        var memory = new Mock<ISharedMemory>();
        memory.Setup(m => m.AddFactAsync(It.IsAny<Fact>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = new MemoryWriteTool(memory.Object, Mock.Of<ILogger<MemoryWriteTool>>());

        var sharedResult = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["type"] = "fact",
            ["agent_id"] = "a1",
            ["category"] = "c1",
            ["content"] = "hello",
            ["visibility"] = "Shared",
            ["shared_with"] = "x, y ,z"
        });

        sharedResult.Success.Should().BeTrue();
        sharedResult.Output.Should().Contain("shared with");

        var rankResult = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["type"] = "fact",
            ["agent_id"] = "a1",
            ["category"] = "c2",
            ["content"] = "hi",
            ["visibility"] = "RankBased",
            ["min_rank"] = "Duke"
        });

        rankResult.Success.Should().BeTrue();
        rankResult.Output.Should().Contain("rank-based");
        rankResult.Output.Should().Contain("Duke+");

        memory.Verify(m => m.AddFactAsync(
            It.Is<Fact>(f => f.Visibility == MemoryVisibility.Shared && f.SharedWithAgents.SequenceEqual(new[] { "x", "y", "z" })),
            It.IsAny<CancellationToken>()), Times.Once);

        memory.Verify(m => m.AddFactAsync(
            It.Is<Fact>(f => f.Visibility == MemoryVisibility.RankBased && f.MinimumRankToView == AgentRank.Duke),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Task_ShouldAddTask_AndDefaultAssignedToAgent()
    {
        var memory = new Mock<ISharedMemory>();
        memory.Setup(m => m.AddTaskAsync(It.IsAny<TaskEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = new MemoryWriteTool(memory.Object, Mock.Of<ILogger<MemoryWriteTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["type"] = "task",
            ["agent_id"] = "a1",
            ["description"] = "do it"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("assigned to: a1");

        memory.Verify(m => m.AddTaskAsync(
            It.Is<TaskEntry>(t => t.CreatedBy == "a1" && t.AssignedTo == "a1" && t.Description == "do it"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenTypeInvalid()
    {
        var memory = new Mock<ISharedMemory>();
        var tool = new MemoryWriteTool(memory.Object, Mock.Of<ILogger<MemoryWriteTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["type"] = "nope",
            ["agent_id"] = "a1"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid type");
    }
}

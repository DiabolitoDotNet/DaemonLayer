using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Tools.Agent;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class CreateSubAgentToolTests
{
    [Fact]
    public async Task ExecuteAsync_WhenPersonaNameMissing_ShouldDeriveNameAndDefaultRankToWorker()
    {
        var logger = new Mock<ILogger<CreateSubAgentTool>>();
        var factory = new Mock<IAgentFactory>(MockBehavior.Strict);
        var loader = new Mock<IPersonaLoader>(MockBehavior.Strict);

        loader
            .Setup(l => l.LoadPersonaAsync("MeteoWorker", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Persona?)null);

        loader
            .Setup(l => l.LoadPersonaAsync("generic_worker", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "generic_worker",
                DemonTitle = "Generic Worker",
                SystemPrompt = "Base prompt",
                Specializations = new List<string> { "general" },
                AvailableTools = new List<string> { "read_memory" }
            });

        var createdAgent = new Mock<IAgent>(MockBehavior.Strict);
        createdAgent.SetupGet(a => a.Id).Returns("a1");
        createdAgent.SetupGet(a => a.Name).Returns("MeteoWorker");
        createdAgent.SetupGet(a => a.Rank).Returns(AgentRank.Worker);
        createdAgent.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        createdAgent.SetupGet(a => a.Persona).Returns(new Persona { Name = "MeteoWorker", DemonTitle = "", SystemPrompt = "", AvailableTools = new List<string> { "read_memory" } });
        createdAgent.Setup(a => a.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        factory
            .Setup(f => f.CreateAgentAsync(
                It.Is<Persona>(p => p.Name == "MeteoWorker" && p.SystemPrompt.Contains("Dynamic assignment", StringComparison.OrdinalIgnoreCase)),
                AgentRank.Worker,
                parentId: null,
                personaPath: It.IsAny<string?>(),
                ct: It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAgent.Object);

        var tool = new CreateSubAgentTool(factory.Object, loader.Object, logger.Object);

        var res = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["role"] = "MeteoWorker",
            ["task"] = "Get tomorrow weather",
            ["description"] = "Weather for Trois-Rivières",
        });

        res.Success.Should().BeTrue();
        res.Metadata["runtime_persona_name"].Should().Be("MeteoWorker");
        res.Metadata["rank"].Should().Be("Worker");

        factory.VerifyAll();
        loader.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_WhenPersonaNameHasAccents_ShouldSanitizeAndFallbackToGenericWorker()
    {
        var logger = new Mock<ILogger<CreateSubAgentTool>>();
        var factory = new Mock<IAgentFactory>(MockBehavior.Strict);
        var loader = new Mock<IPersonaLoader>(MockBehavior.Strict);

        loader
            .Setup(l => l.LoadPersonaAsync("MeteoAgent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Persona?)null);

        loader
            .Setup(l => l.LoadPersonaAsync("generic_worker", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "generic_worker",
                DemonTitle = "Generic Worker",
                SystemPrompt = "Base prompt",
                Specializations = new List<string> { "general" },
                AvailableTools = new List<string> { "read_memory" }
            });

        var createdAgent = new Mock<IAgent>(MockBehavior.Strict);
        createdAgent.SetupGet(a => a.Id).Returns("a2");
        createdAgent.SetupGet(a => a.Name).Returns("MeteoAgent");
        createdAgent.SetupGet(a => a.Rank).Returns(AgentRank.Worker);
        createdAgent.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        createdAgent.SetupGet(a => a.Persona).Returns(new Persona { Name = "MeteoAgent", DemonTitle = "", SystemPrompt = "", AvailableTools = new List<string> { "read_memory" } });
        createdAgent.Setup(a => a.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        factory
            .Setup(f => f.CreateAgentAsync(
                It.Is<Persona>(p => p.Name == "MeteoAgent"),
                AgentRank.Worker,
                parentId: null,
                personaPath: It.IsAny<string?>(),
                ct: It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAgent.Object);

        var tool = new CreateSubAgentTool(factory.Object, loader.Object, logger.Object);

        var res = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["persona_name"] = "MétéoAgent",
            ["task"] = "Trouver les températures météorologiques pour demain à Trois-Rivières",
            ["user_location"] = "Trois-Rivières, Québec"
        });

        res.Success.Should().BeTrue();
        res.Metadata["requested_persona_name"].Should().Be("MétéoAgent");
        res.Metadata["runtime_persona_name"].Should().Be("MeteoAgent");

        factory.VerifyAll();
        loader.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_WhenPersonaExists_ShouldCreateFromSoulsPersona()
    {
        var logger = new Mock<ILogger<CreateSubAgentTool>>();
        var factory = new Mock<IAgentFactory>(MockBehavior.Strict);
        var loader = new Mock<IPersonaLoader>(MockBehavior.Strict);

        loader
            .Setup(l => l.LoadPersonaAsync("Baal", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Persona
            {
                Name = "Baal",
                DemonTitle = "Prince",
                SystemPrompt = "You are Baal",
                AvailableTools = new List<string> { "read_memory" }
            });

        var createdAgent = new Mock<IAgent>(MockBehavior.Strict);
        createdAgent.SetupGet(a => a.Id).Returns("a3");
        createdAgent.SetupGet(a => a.Name).Returns("Baal");
        createdAgent.SetupGet(a => a.Rank).Returns(AgentRank.Prince);
        createdAgent.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        createdAgent.SetupGet(a => a.Persona).Returns(new Persona { Name = "Baal", DemonTitle = "", SystemPrompt = "", AvailableTools = new List<string> { "read_memory" } });
        createdAgent.Setup(a => a.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        factory
            .Setup(f => f.CreateAgentAsync("Baal", AgentRank.Prince, parentId: "lucifer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAgent.Object);

        var tool = new CreateSubAgentTool(factory.Object, loader.Object, logger.Object);

        var res = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["persona_name"] = "Baal",
            ["rank"] = "Prince",
            ["parent_id"] = "lucifer",
        });

        res.Success.Should().BeTrue();
        res.Metadata["runtime_persona_name"].Should().Be("Baal");

        factory.VerifyAll();
        loader.VerifyAll();
    }
}

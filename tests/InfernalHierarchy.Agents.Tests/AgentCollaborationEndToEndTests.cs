using System.Text.RegularExpressions;
using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Messaging.Bus;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public class AgentCollaborationEndToEndTests
{
    [Fact]
    public async Task RequestCollaborationAsync_CollectsResponsesFromAgentsOverMessageBusAsync()
    {
        var loggerBus = new Mock<ILogger<ChannelMessageBus>>().Object;
        var bus = new ChannelMessageBus(loggerBus);

        var registry = new AgentRegistry(new Mock<ILogger<AgentRegistry>>().Object);
        var collaborationService = new AgentCollaborationService(
            new Mock<ILogger<AgentCollaborationService>>().Object,
            bus,
            registry);

        var sharedMemory = new Mock<ISharedMemory>().Object;

        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(tr => tr.GetService<IAgentCollaborationService>()).Returns(collaborationService);

        var agent1 = new CollaborationResponderAgent(
            agent: new Agent { Id = "a1", Name = "a1", Rank = AgentRank.Worker },
            persona: new Persona { Name = "Worker" },
            messageBus: bus,
            sharedMemory: sharedMemory,
            toolRegistry: toolRegistry.Object,
            logger: new Mock<ILogger<BaseAgent>>().Object,
            fixedResponse: "OptionA",
            confidence: 0.9);

        var agent2 = new CollaborationResponderAgent(
            agent: new Agent { Id = "a2", Name = "a2", Rank = AgentRank.Worker },
            persona: new Persona { Name = "Worker" },
            messageBus: bus,
            sharedMemory: sharedMemory,
            toolRegistry: toolRegistry.Object,
            logger: new Mock<ILogger<BaseAgent>>().Object,
            fixedResponse: "OptionA",
            confidence: 0.8);

        registry.Register(agent1);
        registry.Register(agent2);

        await agent1.StartAsync();
        await agent2.StartAsync();

        try
        {
            var request = new CollaborationRequest
            {
                Id = Guid.NewGuid().ToString(),
                InitiatorAgentId = "init",
                Task = "Pick the best option",
                Strategy = CollaborationStrategy.Voting,
                MinimumParticipants = 2,
                MinimumConfidence = 0.6,
                Timeout = TimeSpan.FromSeconds(3),
                ParticipantAgentIds = new List<string> { "a1", "a2" }
            };

            var result = await collaborationService.RequestCollaborationAsync(request);

            result.Decision.Should().Be("OptionA");
            result.ParticipantCount.Should().Be(2);
            result.Responses.Should().HaveCount(2);
            result.Confidence.Should().BeGreaterThan(0);
        }
        finally
        {
            await agent1.StopAsync();
            await agent2.StopAsync();
            bus.Dispose();
        }
    }

    private sealed class CollaborationResponderAgent : BaseAgent
    {
        private static readonly Regex _collaborationRegex = new(
            @"\[COLLABORATION_REQUEST:([^\]]+)\]\s*(.+)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private readonly string _fixedResponse;
        private readonly double _confidence;

        public CollaborationResponderAgent(
            Agent agent,
            Persona persona,
            IMessageBus messageBus,
            ISharedMemory sharedMemory,
            IToolRegistry toolRegistry,
            ILogger<BaseAgent> logger,
            string fixedResponse,
            double confidence)
            : base(agent, persona, messageBus, sharedMemory, toolRegistry, logger)
        {
            _fixedResponse = fixedResponse;
            _confidence = confidence;
        }

        public override async Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default)
        {
            if (!task.Content.StartsWith("[COLLABORATION_REQUEST:", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentMessage
                {
                    FromAgentId = Id,
                    ToAgentId = task.FromAgentId,
                    Type = MessageType.Report,
                    Content = "Ignored"
                };
            }

            var match = _collaborationRegex.Match(task.Content);
            match.Success.Should().BeTrue("collaboration request format should be valid");

            var requestId = match.Groups[1].Value;

            var collaborationService = _toolRegistry.GetService<IAgentCollaborationService>();
            collaborationService.Should().NotBeNull("collaboration service should be available in tool registry");

            await collaborationService!.SubmitResponseAsync(
                requestId,
                new AgentResponse
                {
                    AgentId = Id,
                    AgentRank = Rank,
                    Response = _fixedResponse,
                    Confidence = _confidence,
                    Reasoning = "fixed response",
                    Timestamp = DateTime.UtcNow,
                    ProcessingTimeMs = 1
                },
                ct);

            return new AgentMessage
            {
                FromAgentId = Id,
                ToAgentId = task.FromAgentId,
                Type = MessageType.Report,
                Content = "submitted"
            };
        }
    }
}

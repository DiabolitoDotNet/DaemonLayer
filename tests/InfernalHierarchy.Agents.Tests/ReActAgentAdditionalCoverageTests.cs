using FluentAssertions;
using InfernalHierarchy.Agents.ReAct;
using InfernalHierarchy.Core;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public class ReActAgentAdditionalCoverageTests
{
    private static Agent CreateAgent(string id = "agent-1")
    {
        return new Agent
        {
            Id = id,
            Name = "TestAgent",
            Rank = AgentRank.Duke,
            CreatedAt = DateTime.UtcNow,
            Status = AgentStatus.Idle
        };
    }

    private static Persona CreatePersona(params string[] tools)
    {
        return new Persona
        {
            Name = "TestAgent",
            SystemPrompt = "You are a test agent",
            Specializations = new[] { "testing" },
            AvailableTools = tools.Length == 0 ? Array.Empty<string>() : tools,
            Personality = new PersonalityTraits { Verbosity = 5 }
        };
    }

    private sealed class ExposedReActAgent : ReActAgent
    {
        public ExposedReActAgent(
            Agent agent,
            Persona persona,
            IMessageBus messageBus,
            ISharedMemory sharedMemory,
            IToolRegistry toolRegistry,
            IAgentFactory agentFactory,
            ILlmClient ollamaClient,
            ILogger<ReActAgent> logger,
            IAgentEventSink? eventSink,
            IVectorMemory? vectorMemory,
            RagOptions? ragOptions,
            ReActOptions? reActOptions)
            : base(agent, persona, messageBus, sharedMemory, toolRegistry, agentFactory, ollamaClient, logger, eventSink, vectorMemory, ragOptions, reActOptions)
        {
        }

        public Task<string> ExposeBuildContextAsync(AgentMessage task, CancellationToken ct)
            => base.BuildContextAsync(task, ct);
    }

    [Fact]
    public async Task BuildContextAsync_WhenRagEnabled_AppendsRetrievedFacts_AndTruncatesLongContent()
    {
        var agent = CreateAgent();
        var persona = CreatePersona("x");

        var vectorMemory = new Mock<IVectorMemory>();
        vectorMemory.Setup(x => x.SearchSimilarVisibleFactsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Fact>
            {
                new()
                {
                    Id = "f1",
                    Category = "test",
                    Content = new string('a', 50),
                    Source = "src",
                    Confidence = 0.9,
                    CreatedBy = "agent",
                    CreatedAt = DateTime.UtcNow
                }
            });

        var memory = new Mock<ISharedMemory>();
        memory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());

        var sut = new ExposedReActAgent(
            agent,
            persona,
            Mock.Of<IMessageBus>(),
            memory.Object,
            Mock.Of<IToolRegistry>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<ILlmClient>(),
            Mock.Of<ILogger<ReActAgent>>(),
            eventSink: null,
            vectorMemory: vectorMemory.Object,
            ragOptions: new RagOptions { Enabled = true, MaxCharsPerFact = 10 },
            reActOptions: new ReActOptions { UseJsonResponse = true });

        var context = await sut.ExposeBuildContextAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "find facts"
        }, CancellationToken.None);

        context.Should().Contain("Retrieved Facts (RAG)");
        context.Should().Contain("aaaaaaaaaa…");
    }

    [Fact]
    public async Task BuildContextAsync_WhenRagRetrievalThrows_ReturnsBaseContext()
    {
        var agent = CreateAgent();
        var persona = CreatePersona("x");

        var vectorMemory = new Mock<IVectorMemory>();
        vectorMemory.Setup(x => x.SearchSimilarVisibleFactsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AgentRank>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var memory = new Mock<ISharedMemory>();
        memory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());

        var sut = new ExposedReActAgent(
            agent,
            persona,
            Mock.Of<IMessageBus>(),
            memory.Object,
            Mock.Of<IToolRegistry>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<ILlmClient>(),
            Mock.Of<ILogger<ReActAgent>>(),
            eventSink: null,
            vectorMemory: vectorMemory.Object,
            ragOptions: new RagOptions { Enabled = true },
            reActOptions: new ReActOptions { UseJsonResponse = true });

        var context = await sut.ExposeBuildContextAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "find facts"
        }, CancellationToken.None);

        context.Should().NotContain("Retrieved Facts (RAG)");
        context.Should().Contain("# Current Task");
    }

    [Fact]
    public async Task ProcessTaskAsync_CommandUsage_WhenTrackerMissing_ReturnsWarning_AndSendsViaTelegram()
    {
        var agent = CreateAgent();
        var persona = CreatePersona("telegram_send");

        var toolRegistry = new Mock<IToolRegistry>();

        var telegramTool = new Mock<ITool>();
        telegramTool.SetupGet(x => x.Name).Returns("telegram_send");
        telegramTool.SetupGet(x => x.Description).Returns("send");
        telegramTool.Setup(x => x.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "ok" });

        toolRegistry.Setup(x => x.GetTool("telegram_send")).Returns(telegramTool.Object);

        var sut = new ReActAgent(
            agent,
            persona,
            Mock.Of<IMessageBus>(),
            Mock.Of<ISharedMemory>(),
            toolRegistry.Object,
            Mock.Of<IAgentFactory>(),
            Mock.Of<ILlmClient>(),
            Mock.Of<ILogger<ReActAgent>>());

        var task = new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Command,
            Content = "ignored",
            Payload = new Dictionary<string, object>
            {
                ["command"] = "usage",
                ["telegram_chat_id"] = 1234L
            }
        };

        var response = await sut.ProcessTaskAsync(task, CancellationToken.None);

        response.Content.Should().Contain("Token usage tracking not available");
        telegramTool.Verify(x => x.ExecuteAsync(It.Is<Dictionary<string, object>>(d =>
            Convert.ToInt64(d["chat_id"]) == 1234L && d["message"].ToString()!.Contains("Token usage tracking not available")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessTaskAsync_CollaborationRequest_SubmitsResponseToService()
    {
        var agent = CreateAgent();
        var persona = CreatePersona();

        var memory = new Mock<ISharedMemory>();
        memory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());

        var collaboration = new Mock<IAgentCollaborationService>();
        collaboration.Setup(x => x.SubmitResponseAsync(
                It.IsAny<string>(),
                It.IsAny<AgentResponse>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolRegistry = new Mock<IToolRegistry>();

        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"thought\":\"ok\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"answer\"}");

        var sut = new ReActAgent(
            agent,
            persona,
            Mock.Of<IMessageBus>(),
            memory.Object,
            toolRegistry.Object,
            Mock.Of<IAgentFactory>(),
            llm.Object,
            Mock.Of<ILogger<ReActAgent>>(),
            eventSink: null,
            vectorMemory: null,
            ragOptions: new RagOptions { Enabled = false },
            reActOptions: new ReActOptions { UseJsonResponse = true },
            collaborationService: collaboration.Object);

        var task = new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.CollaborationRequest,
            Content = "[COLLABORATION_REQUEST:req-1] do thing"
        };

        var response = await sut.ProcessTaskAsync(task, CancellationToken.None);

        response.Content.Should().Contain("Collaboration response submitted");
        collaboration.Verify(x => x.SubmitResponseAsync(
            "req-1",
            It.Is<AgentResponse>(r => r.AgentId == agent.Id && r.Response == "answer"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessTaskAsync_WhenRepeatedParseFailures_ReturnsParsingFailureAnswer()
    {
        var agent = CreateAgent();
        var persona = CreatePersona();

        var memory = new Mock<ISharedMemory>();
        memory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());
        memory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var llm = new Mock<ILlmClient>();
        llm.SetupSequence(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Thought: hi")
            .ReturnsAsync("Thought: hi")
            .ReturnsAsync("Thought: hi");

        var sut = new ReActAgent(
            agent,
            persona,
            Mock.Of<IMessageBus>(),
            memory.Object,
            Mock.Of<IToolRegistry>(),
            Mock.Of<IAgentFactory>(),
            llm.Object,
            Mock.Of<ILogger<ReActAgent>>(),
            eventSink: null,
            vectorMemory: null,
            ragOptions: new RagOptions { Enabled = false },
            reActOptions: new ReActOptions { UseJsonResponse = false });

        var response = await sut.ProcessTaskAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "do thing"
        }, CancellationToken.None);

        response.Content.Should().Contain("repeated parsing failures");
    }

    [Fact]
    public async Task ProcessTaskAsync_WhenToolNotFound_ContinuesUntilFinalAnswer()
    {
        var agent = CreateAgent();
        var persona = CreatePersona("missing_tool");

        var memory = new Mock<ISharedMemory>();
        memory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());
        memory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.GetTool("missing_tool")).Returns((ITool?)null);

        var llm = new Mock<ILlmClient>();
        llm.SetupSequence(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"thought\":\"try\",\"action\":\"missing_tool\",\"actionInput\":{}}")
            .ReturnsAsync("{\"thought\":\"done\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"ok\"}");

        var sut = new ReActAgent(
            agent,
            persona,
            Mock.Of<IMessageBus>(),
            memory.Object,
            toolRegistry.Object,
            Mock.Of<IAgentFactory>(),
            llm.Object,
            Mock.Of<ILogger<ReActAgent>>(),
            eventSink: null,
            vectorMemory: null,
            ragOptions: new RagOptions { Enabled = false },
            reActOptions: new ReActOptions { UseJsonResponse = true });

        var response = await sut.ProcessTaskAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "do thing"
        }, CancellationToken.None);

        response.Content.Should().Be("ok");
    }

    [Fact]
    public async Task ProcessTaskAsync_WhenActionInputJsonInvalid_FallsBackToPlainTextParameters_AndAddsAgentContext()
    {
        var agent = CreateAgent();
        var persona = CreatePersona("memory_read");

        var memory = new Mock<ISharedMemory>();
        memory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());
        memory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.GetTool("memory_read")).Returns(Mock.Of<ITool>());
        toolRegistry.Setup(x => x.ExecuteToolWithTrackingAsync(
                "memory_read",
                It.Is<Dictionary<string, object>>(d =>
                    d.ContainsKey("query") &&
                    d.ContainsKey("agent_id") &&
                    d.ContainsKey("agent_rank") &&
                    d["query"].ToString()!.Contains("{not json}")
                ),
                agent.Id,
                agent.Rank.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult { Success = true, Output = "done" });

        var llm = new Mock<ILlmClient>();
        llm.SetupSequence(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Thought: use memory\nAction: memory_read\nAction Input: {not json}")
            .ReturnsAsync("Thought: done\nAction: FINAL_ANSWER\nAction Input: ok");

        var sut = new ReActAgent(
            agent,
            persona,
            Mock.Of<IMessageBus>(),
            memory.Object,
            toolRegistry.Object,
            Mock.Of<IAgentFactory>(),
            llm.Object,
            Mock.Of<ILogger<ReActAgent>>(),
            eventSink: null,
            vectorMemory: null,
            ragOptions: new RagOptions { Enabled = false },
            reActOptions: new ReActOptions { UseJsonResponse = false });

        var response = await sut.ProcessTaskAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "do thing"
        }, CancellationToken.None);

        response.Content.Should().Be("ok");
        toolRegistry.VerifyAll();
    }

    [Fact]
    public async Task ProcessTaskAsync_WhenJsonResponseInsideCodeFence_IsParsed()
    {
        var agent = CreateAgent();
        var persona = CreatePersona();

        var memory = new Mock<ISharedMemory>();
        memory.Setup(x => x.GetRecentDecisionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Decision>());
        memory.Setup(x => x.AddDecisionAsync(It.IsAny<Decision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var llm = new Mock<ILlmClient>();
        llm.Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("```json\n{\"thought\":\"t\",\"action\":\"FINAL_ANSWER\",\"actionInput\":\"ok\"}\n```");

        var sut = new ReActAgent(
            agent,
            persona,
            Mock.Of<IMessageBus>(),
            memory.Object,
            Mock.Of<IToolRegistry>(),
            Mock.Of<IAgentFactory>(),
            llm.Object,
            Mock.Of<ILogger<ReActAgent>>(),
            eventSink: null,
            vectorMemory: null,
            ragOptions: new RagOptions { Enabled = false },
            reActOptions: new ReActOptions { UseJsonResponse = true });

        var response = await sut.ProcessTaskAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "do thing"
        }, CancellationToken.None);

        response.Content.Should().Be("ok");
    }
}

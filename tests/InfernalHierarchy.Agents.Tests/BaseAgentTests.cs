using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public class BaseAgentTests
{
    private sealed class TestMessageBus : IMessageBus
    {
        private readonly Channel<AgentMessage> _inbox = Channel.CreateUnbounded<AgentMessage>();

        public List<AgentMessage> PublishedMessages { get; } = new();
        public TaskCompletionSource<AgentMessage> FirstPublish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishAsync(AgentMessage message, CancellationToken ct = default)
        {
            lock (PublishedMessages)
            {
                PublishedMessages.Add(message);
            }

            FirstPublish.TrySetResult(message);
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<AgentMessage> SubscribeAsync(string agentId, CancellationToken ct = default)
            => _inbox.Reader.ReadAllAsync(ct);

        public IAsyncEnumerable<AgentMessage> SubscribeToBroadcastsAsync(CancellationToken ct = default)
            => Empty(ct);

        public async Task SendAsync(AgentMessage message, CancellationToken ct = default)
        {
            await _inbox.Writer.WriteAsync(message, ct);
        }

        private static async IAsyncEnumerable<AgentMessage> Empty([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class TestAgent : BaseAgent
    {
        private readonly Func<AgentMessage, CancellationToken, Task<AgentMessage>> _handler;
        private int _processCount;

        public int ProcessCount => _processCount;

        public TestAgent(
            Agent agent,
            Persona persona,
            IMessageBus messageBus,
            ISharedMemory sharedMemory,
            IToolRegistry toolRegistry,
            ILogger<BaseAgent> logger,
            Func<AgentMessage, CancellationToken, Task<AgentMessage>> handler)
            : base(agent, persona, messageBus, sharedMemory, toolRegistry, logger)
        {
            _handler = handler;
        }

        public override async Task<AgentMessage> ProcessTaskAsync(AgentMessage task, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _processCount);
            return await _handler(task, ct);
        }

        public Task<string> BuildContextPublicAsync(AgentMessage task, CancellationToken ct = default)
            => BuildContextAsync(task, ct);
    }

    private static (Agent agent, Persona persona) CreateAgent(
        IReadOnlyList<string>? specializations = null,
        IReadOnlyList<string>? availableTools = null)
    {
        return (
            new Agent { Id = "agent-1", Name = "Test", Rank = AgentRank.Worker },
            new Persona
            {
                Name = "test",
                DemonTitle = "",
                SystemPrompt = "sys",
                Specializations = specializations ?? Array.Empty<string>(),
                AvailableTools = availableTools ?? Array.Empty<string>()
            });
    }

    [Fact]
    public async Task StartAsync_ShouldProcessTaskMessages_AndPublishResponse()
    {
        var (agent, persona) = CreateAgent();
        var bus = new TestMessageBus();

        var sut = new TestAgent(
            agent,
            persona,
            bus,
            Mock.Of<ISharedMemory>(),
            Mock.Of<IToolRegistry>(),
            Mock.Of<ILogger<BaseAgent>>(),
            (msg, _) => Task.FromResult(new AgentMessage
            {
                FromAgentId = agent.Id,
                ToAgentId = msg.FromAgentId,
                Type = MessageType.Report,
                Content = "ok"
            }));

        await sut.StartAsync();

        await bus.SendAsync(new AgentMessage
        {
            Id = "m1",
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "hi"
        });

        var published = await bus.FirstPublish.Task.WaitAsync(TimeSpan.FromSeconds(2));
        published.ToAgentId.Should().Be("sender");
        published.Content.Should().Be("ok");

        await sut.StopAsync();
    }

    [Fact]
    public async Task ExecutionLoop_ShouldIgnoreNonTaskMessages()
    {
        var (agent, persona) = CreateAgent();
        var bus = new TestMessageBus();

        var sut = new TestAgent(
            agent,
            persona,
            bus,
            Mock.Of<ISharedMemory>(),
            Mock.Of<IToolRegistry>(),
            Mock.Of<ILogger<BaseAgent>>(),
            (msg, _) => Task.FromResult(msg));

        await sut.StartAsync();

        await bus.SendAsync(new AgentMessage
        {
            Id = "m-notify",
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Notification,
            Content = "ping"
        });

        await Task.Delay(150);

        sut.ProcessCount.Should().Be(0);
        bus.PublishedMessages.Should().BeEmpty();

        await sut.StopAsync();
    }

    [Fact]
    public async Task ExecutionLoop_WhenProcessThrows_ShouldPublishErrorReport()
    {
        var (agent, persona) = CreateAgent();
        var bus = new TestMessageBus();

        var sut = new TestAgent(
            agent,
            persona,
            bus,
            Mock.Of<ISharedMemory>(),
            Mock.Of<IToolRegistry>(),
            Mock.Of<ILogger<BaseAgent>>(),
            (_, _) => Task.FromException<AgentMessage>(new InvalidOperationException("boom")));

        await sut.StartAsync();

        await bus.SendAsync(new AgentMessage
        {
            Id = "m2",
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "do work"
        });

        var published = await bus.FirstPublish.Task.WaitAsync(TimeSpan.FromSeconds(2));
        published.Type.Should().Be(MessageType.Report);
        published.Content.Should().Contain("boom");

        await sut.StopAsync();
    }

    [Fact]
    public async Task SuspendAndResume_ShouldRestartExecutionLoop()
    {
        var (agent, persona) = CreateAgent();
        var bus = new TestMessageBus();

        var sut = new TestAgent(
            agent,
            persona,
            bus,
            Mock.Of<ISharedMemory>(),
            Mock.Of<IToolRegistry>(),
            Mock.Of<ILogger<BaseAgent>>(),
            (msg, _) => Task.FromResult(new AgentMessage
            {
                FromAgentId = agent.Id,
                ToAgentId = msg.FromAgentId,
                Type = MessageType.Report,
                Content = "ok"
            }));

        await sut.StartAsync();
        await sut.SuspendAsync();
        sut.Status.Should().Be(AgentStatus.Suspended);

        await sut.ResumeAsync();
        sut.Status.Should().Be(AgentStatus.Idle);

        await bus.SendAsync(new AgentMessage
        {
            Id = "m-resume",
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "hi again"
        });

        var published = await bus.FirstPublish.Task.WaitAsync(TimeSpan.FromSeconds(2));
        published.ToAgentId.Should().Be("sender");
        published.Content.Should().Be("ok");

        await sut.StopAsync();
    }

    [Fact]
    public async Task SuspendAsync_WhenTerminated_ShouldNoOp()
    {
        var (agent, persona) = CreateAgent();
        var bus = new TestMessageBus();

        var sut = new TestAgent(
            agent,
            persona,
            bus,
            Mock.Of<ISharedMemory>(),
            Mock.Of<IToolRegistry>(),
            Mock.Of<ILogger<BaseAgent>>(),
            (msg, _) => Task.FromResult(msg));

        await sut.StartAsync();
        await sut.StopAsync();
        sut.Status.Should().Be(AgentStatus.Terminated);

        await sut.SuspendAsync();
        sut.Status.Should().Be(AgentStatus.Terminated);
    }

    [Fact]
    public async Task ResumeAsync_WhenNotSuspended_ShouldNoOp()
    {
        var (agent, persona) = CreateAgent();
        var bus = new TestMessageBus();

        var sut = new TestAgent(
            agent,
            persona,
            bus,
            Mock.Of<ISharedMemory>(),
            Mock.Of<IToolRegistry>(),
            Mock.Of<ILogger<BaseAgent>>(),
            (msg, _) => Task.FromResult(msg));

        await sut.StartAsync();
        await sut.ResumeAsync();
        sut.Status.Should().Be(AgentStatus.Idle);
        await sut.StopAsync();
    }

    [Theory]
    [InlineData(AgentRank.Supreme, AgentRank.Supreme, true)]
    [InlineData(AgentRank.Supreme, AgentRank.Worker, true)]
    [InlineData(AgentRank.Prince, AgentRank.Duke, true)]
    [InlineData(AgentRank.Prince, AgentRank.Worker, true)]
    [InlineData(AgentRank.Prince, AgentRank.Prince, false)]
    [InlineData(AgentRank.Duke, AgentRank.Worker, true)]
    [InlineData(AgentRank.Duke, AgentRank.Duke, false)]
    [InlineData(AgentRank.Worker, AgentRank.Worker, false)]
    public void CanCreateSubAgent_ShouldFollowHierarchyRules(AgentRank self, AgentRank target, bool expected)
    {
        var (agent, persona) = CreateAgent();
        agent.Rank = self;
        var bus = new TestMessageBus();

        var sut = new TestAgent(
            agent,
            persona,
            bus,
            Mock.Of<ISharedMemory>(),
            Mock.Of<IToolRegistry>(),
            Mock.Of<ILogger<BaseAgent>>(),
            (msg, _) => Task.FromResult(msg));

        sut.CanCreateSubAgent(target).Should().Be(expected);
    }

    [Fact]
    public async Task BuildContextAsync_WhenNoDecisions_ShouldNotIncludeDecisionsSection()
    {
        var (agent, persona) = CreateAgent(specializations: ["s1", "s2"]);

        var bus = new TestMessageBus();
        var memory = new Mock<ISharedMemory>();
        memory
            .Setup(m => m.GetRecentDecisionsAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = new TestAgent(
            agent,
            persona,
            bus,
            memory.Object,
            Mock.Of<IToolRegistry>(),
            Mock.Of<ILogger<BaseAgent>>(),
            (msg, _) => Task.FromResult(msg));

        var context = await sut.BuildContextPublicAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "do work"
        });

        context.Should().Contain("# System Prompt");
        context.Should().Contain("# Current Task");
        context.Should().NotContain("## Recent Decisions");
    }

    [Fact]
    public async Task BuildContextAsync_WhenDecisionsExist_ShouldIncludeDecisionsSection()
    {
        var (agent, persona) = CreateAgent();
        var bus = new TestMessageBus();
        var memory = new Mock<ISharedMemory>();
        memory
            .Setup(m => m.GetRecentDecisionsAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Decision
                {
                    Context = "ctx",
                    Action = "act",
                    Reasoning = "why",
                    CreatedBy = "agent-1",
                    Outcome = "done"
                }
            ]);

        var sut = new TestAgent(
            agent,
            persona,
            bus,
            memory.Object,
            Mock.Of<IToolRegistry>(),
            Mock.Of<ILogger<BaseAgent>>(),
            (msg, _) => Task.FromResult(msg));

        var context = await sut.BuildContextPublicAsync(new AgentMessage
        {
            FromAgentId = "sender",
            ToAgentId = agent.Id,
            Type = MessageType.Task,
            Content = "do work"
        });

        context.Should().Contain("## Recent Decisions");
        context.Should().Contain("act");
        context.Should().Contain("why");
    }
}

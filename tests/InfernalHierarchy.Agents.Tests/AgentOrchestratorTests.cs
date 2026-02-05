using FluentAssertions;
using InfernalHierarchy.Agents;
using InfernalHierarchy.Core.ErrorHandling;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using System.Reflection;

namespace InfernalHierarchy.Agents.Tests;

public class AgentOrchestratorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCancelled_StartsMainAgent_AndStopsInFinallyAsync()
    {
        var mainAgent = new Mock<IAgent>();
        mainAgent.SetupGet(a => a.Id).Returns("lucifer");
        mainAgent.SetupGet(a => a.Name).Returns("Lucifer");
        mainAgent.SetupGet(a => a.Rank).Returns(AgentRank.Supreme);
        mainAgent.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        mainAgent.SetupGet(a => a.Persona).Returns(new Persona { Name = "Lucifer", DemonTitle = "Lucifer", SystemPrompt = "", Specializations = [] });
        mainAgent.Setup(a => a.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mainAgent.Setup(a => a.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var factory = new Mock<IAgentFactory>();
        factory
            .Setup(f => f.CreateAgentAsync("Lucifer", AgentRank.Supreme, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mainAgent.Object);

        var bus = Mock.Of<IMessageBus>();
        var options = Options.Create(new HierarchyOptions { MainAgentName = "Lucifer" });
        var logger = Mock.Of<ILogger<AgentOrchestrator>>();
        var sp = new ServiceCollection().BuildServiceProvider();

        using var orchestrator = new TestableAgentOrchestrator(factory.Object, bus, options, logger, sp);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await orchestrator.RunAsync(cts.Token);

        factory.Verify(f => f.CreateAgentAsync("Lucifer", AgentRank.Supreme, null, It.IsAny<CancellationToken>()), Times.Once);
        mainAgent.Verify(a => a.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        mainAgent.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFactoryThrows_UsesGlobalExceptionHandler_AndDoesNotThrowAsync()
    {
        var factory = new Mock<IAgentFactory>();
        factory
            .Setup(f => f.CreateAgentAsync(It.IsAny<string>(), It.IsAny<AgentRank>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no persona"));

        var bus = Mock.Of<IMessageBus>();
        var options = Options.Create(new HierarchyOptions { MainAgentName = "Lucifer" });
        var logger = Mock.Of<ILogger<AgentOrchestrator>>();

        var handlerLogger = Mock.Of<ILogger<GlobalExceptionHandler>>();
        var exceptionHandler = new GlobalExceptionHandler(handlerLogger);
        var sp = new ServiceCollection().AddSingleton(exceptionHandler).BuildServiceProvider();

        using var orchestrator = new TestableAgentOrchestrator(factory.Object, bus, options, logger, sp);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var act = async () => await orchestrator.RunAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenFactoryThrows_AndNoGlobalExceptionHandler_ShouldNotThrowAsync()
    {
        var factory = new Mock<IAgentFactory>();
        factory
            .Setup(f => f.CreateAgentAsync(It.IsAny<string>(), It.IsAny<AgentRank>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no persona"));

        var bus = Mock.Of<IMessageBus>();
        var options = Options.Create(new HierarchyOptions { MainAgentName = "Lucifer" });
        var logger = Mock.Of<ILogger<AgentOrchestrator>>();
        var sp = new ServiceCollection().BuildServiceProvider();

        using var orchestrator = new TestableAgentOrchestrator(factory.Object, bus, options, logger, sp);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var act = async () => await orchestrator.RunAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_ShouldStopAllAgents_AndCleanupChannels_WhenUsingChannelMessageBusAsync()
    {
        var a1 = new Mock<IAgent>();
        a1.SetupGet(a => a.Id).Returns("a1");
        a1.SetupGet(a => a.Name).Returns("A1");
        a1.SetupGet(a => a.Rank).Returns(AgentRank.Worker);
        a1.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        a1.SetupGet(a => a.Persona).Returns(new Persona { Name = "A1", DemonTitle = "A1", SystemPrompt = "", Specializations = [] });
        a1.Setup(a => a.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var a2 = new Mock<IAgent>();
        a2.SetupGet(a => a.Id).Returns("a2");
        a2.SetupGet(a => a.Name).Returns("A2");
        a2.SetupGet(a => a.Rank).Returns(AgentRank.Worker);
        a2.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        a2.SetupGet(a => a.Persona).Returns(new Persona { Name = "A2", DemonTitle = "A2", SystemPrompt = "", Specializations = [] });
        a2.Setup(a => a.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var factory = new Mock<IAgentFactory>();
        factory.Setup(f => f.GetAllAgents()).Returns(new[] { a1.Object, a2.Object });

        using var bus = new ChannelMessageBus(Mock.Of<ILogger<ChannelMessageBus>>());
        await bus.PublishAsync(new AgentMessage { FromAgentId = "x", ToAgentId = "a1", Type = MessageType.Task, Content = "hi" });
        await bus.PublishAsync(new AgentMessage { FromAgentId = "x", ToAgentId = "a2", Type = MessageType.Task, Content = "hi" });
        bus.ActiveChannelCount.Should().Be(2);

        var options = Options.Create(new HierarchyOptions { MainAgentName = "Lucifer" });
        var logger = Mock.Of<ILogger<AgentOrchestrator>>();
        var sp = new ServiceCollection().BuildServiceProvider();

        using var orchestrator = new AgentOrchestrator(factory.Object, bus, options, logger, sp);

        await orchestrator.StopAsync(CancellationToken.None);

        a1.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        a2.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        bus.ActiveChannelCount.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_WhenMainAgentStopThrows_ShouldInvokeGlobalExceptionHandler_AndContinueStoppingOthersAsync()
    {
        var main = new Mock<IAgent>();
        main.SetupGet(a => a.Id).Returns("lucifer");
        main.SetupGet(a => a.Name).Returns("Lucifer");
        main.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        main.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("stop failed"));

        var other = new Mock<IAgent>();
        other.SetupGet(a => a.Id).Returns("a2");
        other.SetupGet(a => a.Name).Returns("A2");
        other.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        other.Setup(a => a.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var factory = new Mock<IAgentFactory>();
        factory.Setup(f => f.GetAllAgents()).Returns(new[] { main.Object, other.Object });

        var bus = Mock.Of<IMessageBus>();
        var options = Options.Create(new HierarchyOptions { MainAgentName = "Lucifer" });
        var logger = Mock.Of<ILogger<AgentOrchestrator>>();

        var handler = new TestExceptionHandler(Mock.Of<ILogger<GlobalExceptionHandler>>());
        var sp = new ServiceCollection().AddSingleton<GlobalExceptionHandler>(handler).BuildServiceProvider();

        using var orchestrator = new AgentOrchestrator(factory.Object, bus, options, logger, sp);
        SetMainAgent(orchestrator, main.Object);

        var act = async () => await orchestrator.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        handler.WasHandled.Should().BeTrue();
        other.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_WhenOtherAgentStopThrows_AndNoGlobalExceptionHandler_ShouldSwallowAndStopRemainingAgentsAsync()
    {
        var bad = new Mock<IAgent>();
        bad.SetupGet(a => a.Id).Returns("bad");
        bad.SetupGet(a => a.Name).Returns("Bad");
        bad.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        bad.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var good = new Mock<IAgent>();
        good.SetupGet(a => a.Id).Returns("good");
        good.SetupGet(a => a.Name).Returns("Good");
        good.SetupGet(a => a.Status).Returns(AgentStatus.Idle);
        good.Setup(a => a.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var factory = new Mock<IAgentFactory>();
        factory.Setup(f => f.GetAllAgents()).Returns(new[] { bad.Object, good.Object });

        var bus = Mock.Of<IMessageBus>();
        var options = Options.Create(new HierarchyOptions { MainAgentName = "Lucifer" });
        var logger = Mock.Of<ILogger<AgentOrchestrator>>();
        var sp = new ServiceCollection().BuildServiceProvider();

        using var orchestrator = new AgentOrchestrator(factory.Object, bus, options, logger, sp);

        var act = async () => await orchestrator.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        good.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static void SetMainAgent(AgentOrchestrator orchestrator, IAgent mainAgent)
    {
        var field = typeof(AgentOrchestrator).GetField("_mainAgent", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("AgentOrchestrator should have a private _mainAgent field");
        field!.SetValue(orchestrator, mainAgent);
    }

    private sealed class TestExceptionHandler : GlobalExceptionHandler
    {
        public bool WasHandled { get; private set; }

        public TestExceptionHandler(ILogger<GlobalExceptionHandler> logger) : base(logger)
        {
        }

        protected override Task OnExceptionHandledAsync(Exception exception, ExceptionCategory category, string correlationId, CancellationToken ct)
        {
            WasHandled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestableAgentOrchestrator : AgentOrchestrator
    {
        public TestableAgentOrchestrator(
            IAgentFactory agentFactory,
            IMessageBus messageBus,
            IOptions<HierarchyOptions> options,
            ILogger<AgentOrchestrator> logger,
            IServiceProvider serviceProvider)
            : base(agentFactory, messageBus, options, logger, serviceProvider)
        {
        }

        public Task RunAsync(CancellationToken token) => ExecuteAsync(token);
    }
}

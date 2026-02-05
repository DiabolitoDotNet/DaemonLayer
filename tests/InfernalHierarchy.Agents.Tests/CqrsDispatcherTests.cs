using FluentAssertions;
using InfernalHierarchy.Agents.CQRS;
using InfernalHierarchy.Core.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InfernalHierarchy.Agents.Tests;

public sealed class CqrsDispatcherTests
{
    public sealed record TestCommand(string Name) : ICommand
    {
        public string CommandId { get; init; } = Guid.NewGuid().ToString();

        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public sealed record TestQuery(string Input) : IQuery<string>
    {
        public string QueryId { get; init; } = Guid.NewGuid().ToString();
    }

    [Fact]
    public async Task DispatchCommandAsync_CallsHandler()
    {
        var handler = new Mock<ICommandHandler<TestCommand>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(handler.Object);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CqrsDispatcher(NullLogger<CqrsDispatcher>.Instance, sp);
        var command = new TestCommand("hi");

        await dispatcher.DispatchCommandAsync(command, CancellationToken.None);

        handler.Verify(h => h.HandleAsync(command, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchCommandAsync_WhenHandlerThrows_Rethrows()
    {
        var handler = new Mock<ICommandHandler<TestCommand>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<TestCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var services = new ServiceCollection();
        services.AddSingleton(handler.Object);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CqrsDispatcher(NullLogger<CqrsDispatcher>.Instance, sp);
        var command = new TestCommand("hi");

        var act = async () => await dispatcher.DispatchCommandAsync(command, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task DispatchQueryAsync_WithCacheEnabled_ServesSecondCallFromCache()
    {
        var handler = new Mock<IQueryHandler<TestQuery, string>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestQuery q, CancellationToken _) => $"handled:{q.Input}");

        var services = new ServiceCollection();
        services.AddSingleton(handler.Object);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CqrsDispatcher(NullLogger<CqrsDispatcher>.Instance, sp);
        var query = new TestQuery("q1") { QueryId = "Q" };

        var first = await dispatcher.DispatchQueryAsync<TestQuery, string>(query, useCache: true, CancellationToken.None);
        var second = await dispatcher.DispatchQueryAsync<TestQuery, string>(query, useCache: true, CancellationToken.None);

        first.Should().Be("handled:q1");
        second.Should().Be("handled:q1");
        handler.Verify(h => h.HandleAsync(query, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateCache_RemovesSpecificEntry()
    {
        var handler = new Mock<IQueryHandler<TestQuery, string>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var services = new ServiceCollection();
        services.AddSingleton(handler.Object);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CqrsDispatcher(NullLogger<CqrsDispatcher>.Instance, sp);
        var query = new TestQuery("q") { QueryId = "ID" };

        await dispatcher.DispatchQueryAsync<TestQuery, string>(query, useCache: true, CancellationToken.None);
        dispatcher.InvalidateCache(typeof(TestQuery), "ID");
        await dispatcher.DispatchQueryAsync<TestQuery, string>(query, useCache: true, CancellationToken.None);

        handler.Verify(h => h.HandleAsync(query, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ClearCache_ForcesReexecution()
    {
        var counter = 0;
        var handler = new Mock<IQueryHandler<TestQuery, string>>();
        handler
            .Setup(h => h.HandleAsync(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (++counter).ToString());

        var services = new ServiceCollection();
        services.AddSingleton(handler.Object);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CqrsDispatcher(NullLogger<CqrsDispatcher>.Instance, sp);
        var query = new TestQuery("q") { QueryId = "ID" };

        var a = await dispatcher.DispatchQueryAsync<TestQuery, string>(query, useCache: true, CancellationToken.None);
        var b = await dispatcher.DispatchQueryAsync<TestQuery, string>(query, useCache: true, CancellationToken.None);
        dispatcher.ClearCache();
        var c = await dispatcher.DispatchQueryAsync<TestQuery, string>(query, useCache: true, CancellationToken.None);

        a.Should().Be("1");
        b.Should().Be("1");
        c.Should().Be("2");
    }
}

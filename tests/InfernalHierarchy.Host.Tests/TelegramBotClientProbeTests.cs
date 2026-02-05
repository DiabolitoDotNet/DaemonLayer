using FluentAssertions;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public class TelegramBotClientProbeTests
{
    [Fact]
    public void Ctor_WithClient_ShouldNotThrow()
    {
        var client = new Mock<ITelegramBotClient>(MockBehavior.Strict).Object;

        Action act = () => _ = new TelegramBotClientProbe(client);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetMeAsync_ShouldReturnUser_AndPassCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var expected = new User { Id = 123, FirstName = "Test" };

        CancellationToken observedToken = default;
        var probe = new TelegramBotClientProbe(ct =>
        {
            observedToken = ct;
            return Task.FromResult(expected);
        });

        var user = await probe.GetMeAsync(cts.Token);

        user.Should().BeSameAs(expected);
        observedToken.Should().Be(cts.Token);
    }

    [Fact]
    public void Ctor_WithNullDelegate_ShouldThrow()
    {
        Action act = () => _ = new TelegramBotClientProbe((Func<CancellationToken, Task<User>>)null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

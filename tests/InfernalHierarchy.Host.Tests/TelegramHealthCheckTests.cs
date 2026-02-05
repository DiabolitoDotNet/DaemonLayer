using FluentAssertions;
using InfernalHierarchy.Host;
using InfernalHierarchy.Telegram;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class TelegramHealthCheckCoverageTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenTokenMissing_ShouldReturnDegraded()
    {
        var options = Options.Create(new TelegramOptions { BotToken = " " });
        var factory = new Mock<ITelegramBotClientFactory>(MockBehavior.Strict);

        var sut = new TelegramHealthCheck(options, factory.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTelegramAccessible_ShouldReturnHealthyWithData()
    {
        var botClient = new Mock<ITelegramBotClientProbe>(MockBehavior.Strict);
        botClient
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = 123, IsBot = true, FirstName = "bot", Username = null });

        var factory = new Mock<ITelegramBotClientFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.Create("token"))
            .Returns(botClient.Object);

        var options = Options.Create(new TelegramOptions { BotToken = "token" });
        var sut = new TelegramHealthCheck(options, factory.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("bot_username").WhoseValue.Should().Be("unknown");
        result.Data.Should().ContainKey("bot_id").WhoseValue.Should().Be(123L);
        result.Data.Should().ContainKey("status").WhoseValue.Should().Be("connected");

        factory.VerifyAll();
        botClient.VerifyAll();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTelegramThrows_ShouldReturnUnhealthy()
    {
        var botClient = new Mock<ITelegramBotClientProbe>(MockBehavior.Strict);
        botClient
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var factory = new Mock<ITelegramBotClientFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.Create("token"))
            .Returns(botClient.Object);

        var options = Options.Create(new TelegramOptions { BotToken = "token" });
        var sut = new TelegramHealthCheck(options, factory.Object);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<InvalidOperationException>();

        factory.VerifyAll();
        botClient.VerifyAll();
    }
}

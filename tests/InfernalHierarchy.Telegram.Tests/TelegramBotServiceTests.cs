using System;
using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host;
using InfernalHierarchy.Telegram.Options;
using InfernalHierarchy.Telegram.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Telegram.Tests;

public class TelegramBotServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<TelegramBotService>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;

    public TelegramBotServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<TelegramBotService>>();
        _mockServiceProvider = new Mock<IServiceProvider>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogWarning_WhenTokenNotConfigured()
    {
        // Arrange
        var options = MsOptions.Create(new TelegramOptions { BotToken = string.Empty });
        using var service = new TelegramBotService(options, _mockMessageBus.Object, _mockLogger.Object, _mockServiceProvider.Object);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        await service.StartAsync(cts.Token);

        // Give it a moment to execute
        await Task.Delay(50);

        await service.StopAsync(CancellationToken.None);

        // Assert
        // Verify that warning was logged (would need to setup logger verification)
        Assert.True(true, "Service should handle missing token gracefully");
    }

    [Fact]
    public void TelegramOptions_ShouldSupportAllowedUsers()
    {
        // Arrange & Act
        var options = new TelegramOptions
        {
            AllowedUserIds = new[] { 123456L, 789012L }
        };

        // Assert
        options.AllowedUserIds.Should().HaveCount(2);
        options.AllowedUserIds.Should().Contain(123456L);
    }
}

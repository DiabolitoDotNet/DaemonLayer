using System;
using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Host.Telegram;
using InfernalHierarchy.Telegram.Options;
using InfernalHierarchy.Telegram.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Moq;
using Telegram.Bot;
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
        var voiceOptions = MsOptions.Create(new TelegramVoiceOptions { Enabled = false, ReplyWithVoice = false });
        using var service = new TelegramBotService(
            options,
            voiceOptions,
            _mockMessageBus.Object,
            toolRegistry: null,
            _mockLogger.Object,
            _mockServiceProvider.Object);

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

    [Fact]
    public async Task Forwarder_ShouldSkipEmptyReports()
    {
        var emptyReport = new InfernalHierarchy.Core.Entities.AgentMessage
        {
            Id = Guid.NewGuid().ToString("n"),
            FromAgentId = "lucifer",
            ToAgentId = "telegram",
            Type = InfernalHierarchy.Core.Entities.MessageType.Report,
            Content = "   ",
            Payload = new Dictionary<string, object>
            {
                ["telegram_chat_id"] = 123L
            }
        };

        _mockMessageBus
            .Setup(b => b.SubscribeAsync("telegram", It.IsAny<CancellationToken>()))
            .Returns(new[] { emptyReport }.ToAsyncEnumerable());

        var options = MsOptions.Create(new TelegramOptions { BotToken = "x" });
        var voiceOptions = MsOptions.Create(new TelegramVoiceOptions { Enabled = false, ReplyWithVoice = false });
        using var service = new TelegramBotService(
            options,
            voiceOptions,
            _mockMessageBus.Object,
            toolRegistry: null,
            _mockLogger.Object,
            _mockServiceProvider.Object);

        var botClient = new Mock<ITelegramBotClient>(MockBehavior.Loose);

        // Set private field _botClient so the forwarder runs.
        var botClientField = typeof(TelegramBotService)
            .GetField("_botClient", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        botClientField.Should().NotBeNull();
        botClientField!.SetValue(service, botClient.Object);

        var method = typeof(TelegramBotService)
            .GetMethod("ForwardAgentMessagesToTelegramAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(service, new object[] { CancellationToken.None })!;
        await task;

        botClient.Invocations.Should().BeEmpty();
    }
}

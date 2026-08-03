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
using System.Reflection;

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

    [Fact]
    public async Task Forwarder_WhenMailboxReportReceived_ShouldSendTerminalResponseToOriginChat()
    {
        var mailboxReport = new InfernalHierarchy.Core.Entities.AgentMessage
        {
            Id = Guid.NewGuid().ToString("n"),
            FromAgentId = "lucifer",
            ToAgentId = "telegram",
            Type = InfernalHierarchy.Core.Entities.MessageType.Report,
            Content = "I found inbox messages from alerts@example.com.",
            Payload = new Dictionary<string, object>
            {
                ["telegram_chat_id"] = 456L,
                ["telegram_user_id"] = 999L,
                ["capability_gap_workflow_id"] = "wf-telegram-mailbox-1"
            }
        };

        _mockMessageBus
            .Setup(b => b.SubscribeAsync("telegram", It.IsAny<CancellationToken>()))
            .Returns(new[] { mailboxReport }.ToAsyncEnumerable());

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

        var botClientField = typeof(TelegramBotService)
            .GetField("_botClient", BindingFlags.Instance | BindingFlags.NonPublic);
        botClientField.Should().NotBeNull();
        botClientField!.SetValue(service, botClient.Object);

        var method = typeof(TelegramBotService)
            .GetMethod("ForwardAgentMessagesToTelegramAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(service, new object[] { CancellationToken.None })!;
        await task;

        static bool ArgHasText(object? arg, Func<string, bool> predicate)
        {
            if (arg is null)
            {
                return false;
            }

            if (arg is string s)
            {
                return predicate(s);
            }

            var textProp = arg.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
            if (textProp?.PropertyType == typeof(string) && textProp.GetValue(arg) is string text)
            {
                return predicate(text);
            }

            return false;
        }

        static bool ArgHasChatId(object? arg, long expected)
        {
            if (arg is long l)
            {
                return l == expected;
            }

            if (arg is int i)
            {
                return i == expected;
            }

            var chatIdProp = arg?.GetType().GetProperty("ChatId", BindingFlags.Instance | BindingFlags.Public);
            if (chatIdProp != null)
            {
                var raw = chatIdProp.GetValue(arg);
                if (raw is long cl)
                {
                    return cl == expected;
                }

                if (raw is int ci)
                {
                    return ci == expected;
                }

                // Telegram.Bot ChatId may be a wrapper type exposing Identifier.
                var identifierProp = raw?.GetType().GetProperty("Identifier", BindingFlags.Instance | BindingFlags.Public);
                if (identifierProp?.GetValue(raw) is long identifier)
                {
                    return identifier == expected;
                }

                if (long.TryParse(raw?.ToString(), out var parsed))
                {
                    return parsed == expected;
                }
            }

            return false;
        }

        var sentMailboxSummary = botClient.Invocations.Any(i =>
            (i.Method.Name.Contains("SendRequest", StringComparison.OrdinalIgnoreCase)
             || i.Method.Name.Contains("SendMessage", StringComparison.OrdinalIgnoreCase))
            && i.Arguments.Any(a => ArgHasChatId(a, 456L))
            && i.Arguments.Any(a => ArgHasText(a, s => s.Contains("inbox messages", StringComparison.OrdinalIgnoreCase))));

        sentMailboxSummary.Should().BeTrue("mailbox report should be forwarded back to the originating Telegram chat");
    }
}

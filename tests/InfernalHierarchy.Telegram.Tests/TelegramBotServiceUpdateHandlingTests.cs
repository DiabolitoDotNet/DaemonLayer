using FluentAssertions;
using InfernalHierarchy.Core.ErrorHandling;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Telegram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Reflection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace InfernalHierarchy.Telegram.Tests;

public sealed class TelegramBotServiceUpdateHandlingTests
{
    private static readonly long[] AllowedUserIdsSingle = new[] { 999L };

    private static TelegramBotService CreateService(
        TelegramOptions options,
        IMessageBus messageBus,
        IServiceProvider serviceProvider)
    {
        return new TelegramBotService(
            Options.Create(options),
            messageBus,
            NullLogger<TelegramBotService>.Instance,
            serviceProvider);
    }

    private static Update CreateUpdate(long chatId, long userId, string text)
    {
        var message = new Message
        {
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            From = new User { Id = userId, IsBot = false, FirstName = "U" },
            Text = text
        };

        return new Update
        {
            Message = message
        };
    }

    private static Mock<ITelegramBotClient> CreateBotClientMock()
    {
        var mock = new Mock<ITelegramBotClient>(MockBehavior.Loose);

        // Avoid overload binding issues by setting a default return for Task<Message>.
        mock.SetReturnsDefault(Task.FromResult(new Message()));

        return mock;
    }

    [Fact]
    public async Task HandleUpdateAsync_WhenUserNotAllowed_SendsUnauthorizedAndDoesNotPublish()
    {
        var messageBus = new Mock<IMessageBus>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        using var service = CreateService(
            new TelegramOptions { BotToken = "x", AllowedUserIds = AllowedUserIdsSingle },
            messageBus.Object,
            serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "hello");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        messageBus.Verify(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleUpdateAsync_WithPlainText_PublishesTaskToLuciferAndAcknowledges()
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        using var service = CreateService(
            new TelegramOptions { BotToken = "x", AllowedUserIds = Array.Empty<long>() },
            messageBus.Object,
            serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "do the thing");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].FromAgentId.Should().Be("telegram");
        published[0].ToAgentId.Should().Be("lucifer");
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Task);
        published[0].Content.Should().Be("do the thing");
        published[0].Payload.Should().ContainKey("telegram_chat_id");
        published[0].Payload.Should().ContainKey("telegram_user_id");
    }

    [Fact]
    public async Task HandleUpdateAsync_WithStatusCommand_PublishesQuery()
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "/status");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Query);
        published[0].Content.Should().Be("status");
        published[0].ToAgentId.Should().Be("lucifer");
    }

    [Fact]
    public async Task HandleUpdateAsync_WhenPublishThrows_UsesGlobalExceptionHandlerAndSendsFriendlyMessage()
    {
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timeout"));

        var services = new ServiceCollection();
        services.AddSingleton(new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance));
        var serviceProvider = services.BuildServiceProvider();

        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "plain text");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);
    }

    [Fact]
    public async Task HandleUpdateAsync_WhenUpdateHasNoMessage_DoesNothing()
    {
        var messageBus = new Mock<IMessageBus>(MockBehavior.Strict);
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);
        var botClient = CreateBotClientMock();

        await service.HandleUpdateAsync(botClient.Object, new Update(), CancellationToken.None);
    }

    [Fact]
    public async Task HandleUpdateAsync_WhenMessageHasNoText_DoesNothing()
    {
        var messageBus = new Mock<IMessageBus>(MockBehavior.Strict);
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);
        var botClient = CreateBotClientMock();

        var update = new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = 123, Type = ChatType.Private },
                From = new User { Id = 111, IsBot = false, FirstName = "U" },
                Text = null
            }
        };

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);
    }

    [Theory]
    [InlineData("/start")]
    [InlineData("/help")]
    [InlineData("/unknown")]
    [InlineData("/summon")]
    [InlineData("/summon Paimon not_a_rank")]
    [InlineData("/kill")]
    [InlineData("/suspend")]
    [InlineData("/resume")]
    public async Task HandleUpdateAsync_ForCommandsThatDoNotPublish_DoesNotPublish(string command)
    {
        var messageBus = new Mock<IMessageBus>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);
        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: command);

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        messageBus.Verify(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleUpdateAsync_WithSummonCommand_ValidRank_PublishesCreateSubAgentCommand()
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        using var service = CreateService(
            new TelegramOptions { BotToken = "x" },
            messageBus.Object,
            serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "/summon Paimon duke");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Command);
        published[0].ToAgentId.Should().Be("lucifer");
        published[0].Content.Should().Be("create_sub_agent");
        published[0].Payload.Should().ContainKey("demon_name");
        published[0].Payload["demon_name"].Should().Be("Paimon");
        published[0].Payload.Should().ContainKey("rank");
        published[0].Payload["rank"].Should().Be(AgentRank.Duke.ToString());
    }

    [Fact]
    public async Task HandleUpdateAsync_WithKillCommand_PublishesTerminateCommandToAgent()
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "/kill agent_123");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Command);
        published[0].ToAgentId.Should().Be("agent_123");
        published[0].Content.Should().Be("terminate");
        published[0].Payload.Should().ContainKey("command");
        published[0].Payload["command"].Should().Be("kill");
    }

    [Theory]
    [InlineData("/memory", "")]
    [InlineData("/memory foo bar", "foo bar")]
    public async Task HandleUpdateAsync_WithMemoryCommand_PublishesReadMemoryQuery(string command, string expectedQuery)
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: command);

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Query);
        published[0].ToAgentId.Should().Be("lucifer");
        published[0].Content.Should().Be("read_memory");
        published[0].Payload.Should().ContainKey("query");
        published[0].Payload["query"].Should().Be(expectedQuery);
    }

    [Fact]
    public async Task HandleUpdateAsync_WithUsageCommand_PublishesTokenUsageQuery()
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "/usage");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Query);
        published[0].ToAgentId.Should().Be("lucifer");
        published[0].Content.Should().Be("token_usage");
    }

    [Theory]
    [InlineData("/learning", "")]
    [InlineData("/learning agent_123", "agent_123")]
    public async Task HandleUpdateAsync_WithLearningCommand_PublishesLearningStatsQuery(string command, string expectedAgentId)
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: command);

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Query);
        published[0].ToAgentId.Should().Be("lucifer");
        published[0].Content.Should().Be("learning_stats");
        published[0].Payload.Should().ContainKey("agent_id");
        published[0].Payload["agent_id"].Should().Be(expectedAgentId);
    }

    [Fact]
    public async Task HandleUpdateAsync_WithModelsCommand_PublishesListModelsQuery()
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "/models");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Query);
        published[0].ToAgentId.Should().Be("lucifer");
        published[0].Content.Should().Be("list_models");
    }

    [Fact]
    public async Task HandleUpdateAsync_WithSuspendCommand_PublishesSuspendCommandToAgent()
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "/suspend agent_123");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Command);
        published[0].ToAgentId.Should().Be("agent_123");
        published[0].Content.Should().Be("suspend");
    }

    [Fact]
    public async Task HandleUpdateAsync_WithResumeCommand_PublishesResumeCommandToAgent()
    {
        var published = new List<AgentMessage>();
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<AgentMessage, CancellationToken>((msg, _) => published.Add(msg))
            .Returns(Task.CompletedTask);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "/resume agent_123");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        published.Should().ContainSingle();
        published[0].Type.Should().Be(InfernalHierarchy.Core.Entities.MessageType.Command);
        published[0].ToAgentId.Should().Be("agent_123");
        published[0].Content.Should().Be("resume");
    }

    [Fact]
    public async Task HandleUpdateAsync_WhenCommandPublishThrows_IsHandledByCommandHandler()
    {
        var messageBus = new Mock<IMessageBus>();
        messageBus
            .Setup(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();
        var update = CreateUpdate(chatId: 123, userId: 111, text: "/status");

        await service.HandleUpdateAsync(botClient.Object, update, CancellationToken.None);

        messageBus.Verify(b => b.PublishAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_WhenBotClientInitialized_UsesClient()
    {
        var messageBus = new Mock<IMessageBus>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        var botClient = CreateBotClientMock();

        var field = typeof(TelegramBotService).GetField("_botClient", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(service, botClient.Object);

        await service.SendMessageAsync(123, "hi", CancellationToken.None);
    }

    [Fact]
    public async Task SendMessageAsync_WhenBotClientNotInitialized_DoesNotThrow()
    {
        var messageBus = new Mock<IMessageBus>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "x" }, messageBus.Object, serviceProvider);

        await service.SendMessageAsync(123, "hi", CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenBotTokenEmpty_DoesNotInitializeClient()
    {
        var messageBus = new Mock<IMessageBus>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        using var service = CreateService(new TelegramOptions { BotToken = "" }, messageBus.Object, serviceProvider);

        await service.StartAsync(CancellationToken.None);

        var field = typeof(TelegramBotService).GetField("_botClient", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.GetValue(service).Should().BeNull();
    }
}

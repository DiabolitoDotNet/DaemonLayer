using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Tools.Telegram;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class TelegramSendToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenChatIdMissing()
    {
        var sender = new Mock<ITelegramMessageSender>(MockBehavior.Strict);
        var tool = new TelegramSendTool(Mock.Of<ILogger<TelegramSendTool>>(), sender.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["text"] = "hi" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("chat_id");
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenTextMissing()
    {
        var sender = new Mock<ITelegramMessageSender>(MockBehavior.Strict);
        var tool = new TelegramSendTool(Mock.Of<ILogger<TelegramSendTool>>(), sender.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["chat_id"] = 123L });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("text");
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenSenderFails()
    {
        var sender = new Mock<ITelegramMessageSender>();
        sender.Setup(x => x.SendMessageAsync(123L, "hello", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TelegramSendResult.Fail(123L, "transport failure", retryable: true, TimeSpan.FromMilliseconds(22)));

        var tool = new TelegramSendTool(Mock.Of<ILogger<TelegramSendTool>>(), sender.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["chat_id"] = 123L,
            ["text"] = "hello"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("transport failure");
        result.Metadata.Should().NotBeNull();
        result.Metadata!["chat_id"].Should().Be(123L);
        result.Metadata["retryable"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSucceed_WhenUsingAliasKeys()
    {
        var sender = new Mock<ITelegramMessageSender>();
        sender.Setup(x => x.SendMessageAsync(123L, "hello", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TelegramSendResult.Ok(123L, 42, TimeSpan.FromMilliseconds(9)));

        var tool = new TelegramSendTool(Mock.Of<ILogger<TelegramSendTool>>(), sender.Object);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["telegram_chat_id"] = 123L,
            ["message"] = "hello"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("123");
        result.Metadata.Should().NotBeNull();
        result.Metadata!["chat_id"].Should().Be(123L);
        result.Metadata["message_id"].Should().Be(42);
        result.Metadata["delivery_status"].Should().Be("sent");
    }
}

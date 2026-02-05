using FluentAssertions;
using InfernalHierarchy.Tools;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class TelegramSendToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenChatIdMissing()
    {
        var tool = new TelegramSendTool(Mock.Of<ILogger<TelegramSendTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["text"] = "hi" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("chat_id");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnError_WhenTextMissing()
    {
        var tool = new TelegramSendTool(Mock.Of<ILogger<TelegramSendTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["chat_id"] = 123L });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("text");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSucceed_WhenParametersValid()
    {
        var tool = new TelegramSendTool(Mock.Of<ILogger<TelegramSendTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["chat_id"] = 123L,
            ["text"] = "hello"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("123");
        result.Metadata.Should().NotBeNull();
        result.Metadata!["chat_id"].Should().Be(123L);
        result.Metadata["text"].Should().Be("hello");
    }
}

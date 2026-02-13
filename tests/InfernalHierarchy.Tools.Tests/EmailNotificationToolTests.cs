using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Notifications;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace InfernalHierarchy.Tools.Tests;

public sealed class EmailNotificationToolTests
{
    private sealed class CapturingSender : IEmailSender
    {
        public MailMessage? LastMessage { get; private set; }

        public Task SendAsync(MailMessage message, CancellationToken ct)
        {
            LastMessage = message;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSender : IEmailSender
    {
        public Task SendAsync(MailMessage message, CancellationToken ct)
        {
            throw new InvalidOperationException("smtp down");
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ReturnsFailure()
    {
        var options = MsOptions.Create(new EmailNotificationOptions { Enabled = false });
        var sender = new CapturingSender();
        var tool = new EmailNotificationTool(options, sender, NullLogger<EmailNotificationTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "a@b.com",
            ["subject"] = "s",
            ["body"] = "b"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disabled");
        sender.LastMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("to")]
    [InlineData("subject")]
    [InlineData("body")]
    public async Task ExecuteAsync_WhenMissingRequiredParam_ReturnsFailure(string missing)
    {
        var options = MsOptions.Create(new EmailNotificationOptions
        {
            Enabled = true,
            FromAddress = "from@example.com"
        });

        var sender = new CapturingSender();
        var tool = new EmailNotificationTool(options, sender, NullLogger<EmailNotificationTool>.Instance);

        var parameters = new Dictionary<string, object>
        {
            ["to"] = "a@b.com",
            ["subject"] = "s",
            ["body"] = "b"
        };
        parameters.Remove(missing);

        var result = await tool.ExecuteAsync(parameters);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain($"{missing}");
        sender.LastMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvalidTo_ReturnsFailure()
    {
        var options = MsOptions.Create(new EmailNotificationOptions
        {
            Enabled = true,
            FromAddress = "from@example.com"
        });

        var sender = new CapturingSender();
        var tool = new EmailNotificationTool(options, sender, NullLogger<EmailNotificationTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "not-an-email",
            ["subject"] = "s",
            ["body"] = "b"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid 'to'");
        sender.LastMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvalidToAndDefaultToConfigured_FallsBackToDefaultToAndSends()
    {
        var options = MsOptions.Create(new EmailNotificationOptions
        {
            Enabled = true,
            FromAddress = "from@example.com",
            DefaultTo = "default@x.com"
        });

        var sender = new CapturingSender();
        var tool = new EmailNotificationTool(options, sender, NullLogger<EmailNotificationTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "http-req-123",
            ["subject"] = "s",
            ["body"] = "b"
        });

        result.Success.Should().BeTrue();
        sender.LastMessage.Should().NotBeNull();
        sender.LastMessage!.To.Should().HaveCount(1);
        sender.LastMessage.To[0].Address.Should().Be("default@x.com");
    }

    [Fact]
    public async Task ExecuteAsync_WhenSuccess_SendsMailMessage_WithHtmlAndCcBcc()
    {
        var options = MsOptions.Create(new EmailNotificationOptions
        {
            Enabled = true,
            FromAddress = "from@example.com",
            FromName = "Infernal"
        });

        var sender = new CapturingSender();
        var tool = new EmailNotificationTool(options, sender, NullLogger<EmailNotificationTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "a@b.com; c@d.com",
            ["cc"] = "cc1@x.com",
            ["bcc"] = "bcc1@x.com",
            ["subject"] = "Hello",
            ["body"] = "<b>world</b>",
            ["is_html"] = true
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Email sent");

        sender.LastMessage.Should().NotBeNull();
        var msg = sender.LastMessage!;
        msg.From.Should().NotBeNull();
        var from = msg.From!;
        from.Address.Should().Be("from@example.com");
        from.DisplayName.Should().Be("Infernal");
        msg.Subject.Should().Be("Hello");
        msg.Body.Should().Be("<b>world</b>");
        msg.IsBodyHtml.Should().BeTrue();
        msg.To.Should().HaveCount(2);
        msg.CC.Should().HaveCount(1);
        msg.Bcc.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUsingRecipientAndMessageAliases_Succeeds()
    {
        var options = MsOptions.Create(new EmailNotificationOptions
        {
            Enabled = true,
            FromAddress = "from@example.com"
        });

        var sender = new CapturingSender();
        var tool = new EmailNotificationTool(options, sender, NullLogger<EmailNotificationTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["recipient"] = "a@b.com",
            ["subject"] = "s",
            ["message"] = "b"
        });

        result.Success.Should().BeTrue();
        sender.LastMessage.Should().NotBeNull();
        sender.LastMessage!.To.Should().HaveCount(1);
        sender.LastMessage.To[0].Address.Should().Be("a@b.com");
        sender.LastMessage.Subject.Should().Be("s");
        sender.LastMessage.Body.Should().Be("b");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecipientIsPlaceholder_UsesDefaultTo()
    {
        var options = MsOptions.Create(new EmailNotificationOptions
        {
            Enabled = true,
            FromAddress = "from@example.com",
            DefaultTo = "default@x.com"
        });

        var sender = new CapturingSender();
        var tool = new EmailNotificationTool(options, sender, NullLogger<EmailNotificationTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["recipient"] = "USER_EMAIL",
            ["title"] = "hello",
            ["message"] = "body"
        });

        result.Success.Should().BeTrue();
        sender.LastMessage.Should().NotBeNull();
        sender.LastMessage!.To.Should().HaveCount(1);
        sender.LastMessage.To[0].Address.Should().Be("default@x.com");
        sender.LastMessage.Subject.Should().Be("hello");
        sender.LastMessage.Body.Should().Be("body");
    }

    [Fact]
    public async Task ExecuteAsync_WhenBodyContainsTemplatePlaceholders_ReturnsFailureAndDoesNotSend()
    {
        var options = MsOptions.Create(new EmailNotificationOptions
        {
            Enabled = true,
            FromAddress = "from@example.com"
        });

        var sender = new CapturingSender();
        var tool = new EmailNotificationTool(options, sender, NullLogger<EmailNotificationTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "a@b.com",
            ["subject"] = "s",
            ["body"] = "There are ${total_agents} agents"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("placeholder");
        sender.LastMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenSenderThrows_ReturnsFailureWithError()
    {
        var options = MsOptions.Create(new EmailNotificationOptions
        {
            Enabled = true,
            FromAddress = "from@example.com"
        });

        var sender = new ThrowingSender();
        var tool = new EmailNotificationTool(options, sender, NullLogger<EmailNotificationTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["to"] = "a@b.com",
            ["subject"] = "s",
            ["body"] = "b"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("smtp down");
    }
}

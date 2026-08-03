using FluentAssertions;
using InfernalHierarchy.Tools.Notifications;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class EmailInboxQueryToolTests
{
    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ShouldReturnError()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EmailInboxQueryOptions { Enabled = false });
        var tool = new EmailInboxQueryTool(options, Mock.Of<IEmailInboxQueryClient>(), Mock.Of<ILogger<EmailInboxQueryTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disabled");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCredentialInjectedInParameters_ShouldReject()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EmailInboxQueryOptions
        {
            Enabled = true,
            Host = "imap.example.com",
            Username = "reader@example.com",
            Password = "secret"
        });

        var tool = new EmailInboxQueryTool(options, Mock.Of<IEmailInboxQueryClient>(), Mock.Of<ILogger<EmailInboxQueryTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["password"] = "dont-send-here"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Credentials must come from secure configuration references");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidParameters_ShouldQueryClientAndReturnSummaries()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EmailInboxQueryOptions
        {
            Enabled = true,
            Host = "imap.example.com",
            Port = 993,
            UseSsl = true,
            Username = "reader@example.com",
            Password = "secret",
            MaxResults = 10
        });

        var client = new Mock<IEmailInboxQueryClient>();
        client.Setup(x => x.QueryAsync(It.IsAny<EmailInboxQueryOptions>(), It.IsAny<EmailInboxQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmailInboxMessageSummary(
                    Id: "1",
                    From: "alerts@example.com",
                    Subject: "Alert",
                    DateUtc: DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
                    Unread: true)
            });

        var tool = new EmailInboxQueryTool(options, client.Object, Mock.Of<ILogger<EmailInboxQueryTool>>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["from"] = "alerts@example.com",
            ["unread_only"] = true,
            ["max_results"] = 5
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("alerts@example.com");
        result.Metadata["read_only"].Should().Be(true);
        result.Metadata["provider"].Should().Be("imap");

        client.Verify(x => x.QueryAsync(
            It.IsAny<EmailInboxQueryOptions>(),
            It.Is<EmailInboxQueryRequest>(r =>
                r.FromFilter == "alerts@example.com"
                && r.UnreadOnly
                && r.MaxResults == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

using FluentAssertions;
using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Host.Configuration;
using InfernalHierarchy.Host.Hosting;
using InfernalHierarchy.Host.Observability;
using InfernalHierarchy.Telegram.Options;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class AutonomyReadinessHostedServiceTests
{
    [Fact]
    public async Task StartAsync_WhenCriticalCapabilityConfigured_ShouldMarkReady()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.GetTool("email_inbox_query")).Returns(Mock.Of<ITool>());
        var store = new AutonomyReadinessReportStore();

        var service = new AutonomyReadinessHostedService(
            NullLogger<AutonomyReadinessHostedService>.Instance,
            toolRegistry.Object,
            Options.Create(new AutonomyReadinessOptions
            {
                Enabled = true,
                FailStartupOnCriticalNotReady = false,
                CriticalCapabilities = new[] { "email_inbox_query" }
            }),
            Options.Create(new EmailInboxQueryOptions
            {
                Enabled = true,
                Host = "imap.example.com",
                Username = "reader@example.com",
                Password = "secret"
            }),
            Options.Create(new EmailNotificationOptions
            {
                Enabled = true,
                Host = "smtp.example.com",
                Username = "sender@example.com",
                Password = "secret",
                FromAddress = "sender@example.com"
            }),
            Options.Create(new TelegramOptions
            {
                BotToken = "token"
            }),
            store);

        await service.StartAsync(CancellationToken.None);

        var report = store.GetCurrent();
        report.AllCriticalReady.Should().BeTrue();
        report.Items.Should().ContainSingle(i => i.Capability == "email_inbox_query" && i.Ready);
    }

    [Fact]
    public async Task StartAsync_WhenCriticalCapabilityMissingAndFailStartupEnabled_ShouldThrow()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.GetTool("email_inbox_query")).Returns((ITool?)null);

        var service = new AutonomyReadinessHostedService(
            NullLogger<AutonomyReadinessHostedService>.Instance,
            toolRegistry.Object,
            Options.Create(new AutonomyReadinessOptions
            {
                Enabled = true,
                FailStartupOnCriticalNotReady = true,
                CriticalCapabilities = new[] { "email_inbox_query" }
            }),
            Options.Create(new EmailInboxQueryOptions
            {
                Enabled = false
            }),
            Options.Create(new EmailNotificationOptions
            {
                Enabled = false
            }),
            Options.Create(new TelegramOptions()),
            new AutonomyReadinessReportStore());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_WhenEmailSendConfigured_ShouldMarkReady()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.GetTool("email_send")).Returns(Mock.Of<ITool>());
        var store = new AutonomyReadinessReportStore();

        var service = new AutonomyReadinessHostedService(
            NullLogger<AutonomyReadinessHostedService>.Instance,
            toolRegistry.Object,
            Options.Create(new AutonomyReadinessOptions
            {
                Enabled = true,
                CriticalCapabilities = ["email_send"]
            }),
            Options.Create(new EmailInboxQueryOptions()),
            Options.Create(new EmailNotificationOptions
            {
                Enabled = true,
                Host = "smtp.example.com",
                Username = "sender@example.com",
                Password = "secret",
                FromAddress = "sender@example.com"
            }),
            Options.Create(new TelegramOptions()),
            store);

        await service.StartAsync(CancellationToken.None);

        var report = store.GetCurrent();
        report.AllCriticalReady.Should().BeTrue();
        report.Items.Should().ContainSingle(i => i.Capability == "email_send" && i.Ready);
    }

    [Fact]
    public async Task StartAsync_WhenTelegramMissingToken_ShouldMarkNotReady()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry.Setup(x => x.GetTool("send_telegram")).Returns(Mock.Of<ITool>());
        var store = new AutonomyReadinessReportStore();

        var service = new AutonomyReadinessHostedService(
            NullLogger<AutonomyReadinessHostedService>.Instance,
            toolRegistry.Object,
            Options.Create(new AutonomyReadinessOptions
            {
                Enabled = true,
                CriticalCapabilities = ["send_telegram"]
            }),
            Options.Create(new EmailInboxQueryOptions()),
            Options.Create(new EmailNotificationOptions()),
            Options.Create(new TelegramOptions
            {
                BotToken = string.Empty
            }),
            store);

        await service.StartAsync(CancellationToken.None);

        var report = store.GetCurrent();
        report.AllCriticalReady.Should().BeFalse();
        report.Items.Should().ContainSingle(i => i.Capability == "send_telegram" && !i.Ready && i.Reason == "configuration_incomplete_or_disabled");
    }
}

using System.Net.Mail;
using FluentAssertions;
using InfernalHierarchy.Host.Resilience;
using InfernalHierarchy.Host.Security;
using InfernalHierarchy.Tools.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace InfernalHierarchy.Host.Tests;

public sealed class ResilientEmailSenderTests
{
    [Fact]
    public async Task SendAsync_WhenTransientFailure_ShouldRetryAndSucceed()
    {
        var calls = 0;
        var inner = new DelegateEmailSender(async (_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("transient test failure");
            }

            await Task.CompletedTask;
        });

        var policies = new ResiliencePolicies(NullLogger<ResiliencePolicies>.Instance);
        var provider = new ResiliencePolicyProvider(policies);
        var sut = new ResilientEmailSender(inner, provider, NullLogger<ResilientEmailSender>.Instance);

        using var message = new MailMessage("from@example.com", "to@example.com", "subject", "body");
        await sut.SendAsync(message, CancellationToken.None);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_WhenPermanentArgumentException_ShouldNotRetry()
    {
        var calls = 0;
        var inner = new DelegateEmailSender((_, _) =>
        {
            calls++;
            throw new ArgumentException("permanent");
        });

        var policies = new ResiliencePolicies(NullLogger<ResiliencePolicies>.Instance);
        var provider = new ResiliencePolicyProvider(policies);
        var sut = new ResilientEmailSender(inner, provider, NullLogger<ResilientEmailSender>.Instance);

        using var message = new MailMessage("from@example.com", "to@example.com", "subject", "body");

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SendAsync(message, CancellationToken.None));
        calls.Should().Be(1);
    }

    private sealed class DelegateEmailSender : IEmailSender
    {
        private readonly Func<MailMessage, CancellationToken, Task> _handler;

        public DelegateEmailSender(Func<MailMessage, CancellationToken, Task> handler)
        {
            _handler = handler;
        }

        public Task SendAsync(MailMessage message, CancellationToken ct) => _handler(message, ct);
    }
}

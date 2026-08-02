using Polly;
using System.Net.Mail;

namespace InfernalHierarchy.Host.Security;

internal sealed class ResilientEmailSender : IEmailSender
{
    private readonly IEmailSender _inner;
    private readonly IAsyncPolicy _policy;
    private readonly ILogger<ResilientEmailSender> _logger;

    public ResilientEmailSender(
        IEmailSender inner,
        IResiliencePolicyProvider resilience,
        ILogger<ResilientEmailSender> logger)
    {
        _inner = inner;
        _policy = resilience.GetToolExecutionPolicy();
        _logger = logger;
    }

    public Task SendAsync(MailMessage message, CancellationToken ct)
    {
        return _policy.ExecuteAsync(async token =>
        {
            await _inner.SendAsync(message, token).ConfigureAwait(false);
        }, ct);
    }
}

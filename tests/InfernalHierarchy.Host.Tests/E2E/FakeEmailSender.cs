using System.Collections.Concurrent;
using System.Net.Mail;
using InfernalHierarchy.Tools.Notifications;

namespace InfernalHierarchy.Host.Tests.E2E;

public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentBag<MailMessage> _sent = new();

    public IReadOnlyCollection<MailMessage> Sent => _sent.ToArray();

    public Task SendAsync(MailMessage message, CancellationToken ct)
    {
        // Clone minimal data because MailMessage is IDisposable and will be disposed by the tool.
        var clone = new MailMessage
        {
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = message.IsBodyHtml,
        };

        if (message.From is not null)
        {
            clone.From = message.From;
        }

        foreach (var to in message.To)
        {
            clone.To.Add(to);
        }

        foreach (var cc in message.CC)
        {
            clone.CC.Add(cc);
        }

        foreach (var bcc in message.Bcc)
        {
            clone.Bcc.Add(bcc);
        }

        _sent.Add(clone);
        return Task.CompletedTask;
    }
}

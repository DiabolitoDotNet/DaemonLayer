using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace InfernalHierarchy.Tools.Notifications;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailNotificationOptions _options;

    public SmtpEmailSender(IOptions<EmailNotificationOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(MailMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.Username, _options.Password),
            Timeout = _options.TimeoutMs
        };

        using (ct.Register(() => client.SendAsyncCancel()))
        {
            await client.SendMailAsync(message).ConfigureAwait(false);
        }
    }
}

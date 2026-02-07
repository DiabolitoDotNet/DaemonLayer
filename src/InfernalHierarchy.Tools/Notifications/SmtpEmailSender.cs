using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Mail;

namespace InfernalHierarchy.Tools.Notifications;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailNotificationOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailNotificationOptions> options, ILogger<SmtpEmailSender>? logger = null)
    {
        _options = options.Value;
        _logger = logger ?? NullLogger<SmtpEmailSender>.Instance;
    }

    public async Task SendAsync(MailMessage message, CancellationToken ct)
    {
        var toMasked = MaskAddresses(message.To);
        var ccMasked = message.CC.Count > 0 ? MaskAddresses(message.CC) : string.Empty;
        var bccMasked = message.Bcc.Count > 0 ? MaskAddresses(message.Bcc) : string.Empty;

        _logger.LogInformation(
            "📧 SMTP sending email | Host={Host} Port={Port} Ssl={Ssl} TimeoutMs={TimeoutMs} From={From} To={To}{CcPart}{BccPart} Subject={Subject}",
            _options.Host,
            _options.Port,
            _options.UseSsl,
            _options.TimeoutMs,
            message.From?.Address ?? string.Empty,
            toMasked,
            string.IsNullOrWhiteSpace(ccMasked) ? string.Empty : $" Cc={ccMasked}",
            string.IsNullOrWhiteSpace(bccMasked) ? string.Empty : $" Bcc={bccMasked}",
            message.Subject);

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
            try
            {
                await client.SendMailAsync(message).ConfigureAwait(false);
                _logger.LogInformation("✅ SMTP email sent | Host={Host} Port={Port} To={To} Subject={Subject}",
                    _options.Host,
                    _options.Port,
                    toMasked,
                    message.Subject);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "❌ SMTP email send failed | Host={Host} Port={Port} Ssl={Ssl} To={To} Subject={Subject}",
                    _options.Host,
                    _options.Port,
                    _options.UseSsl,
                    toMasked,
                    message.Subject);
                throw;
            }
        }
    }

    private static string MaskAddresses(MailAddressCollection addresses)
    {
        if (addresses.Count == 0) return string.Empty;

        // Avoid logging full PII in normal INFO logs; keep it useful for troubleshooting.
        return string.Join(", ", addresses.Select(a => MaskEmail(a.Address)));
    }

    private static string MaskEmail(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return string.Empty;

        var at = address.IndexOf('@');
        if (at <= 0 || at == address.Length - 1) return "***";

        var local = address[..at];
        var domain = address[(at + 1)..];

        var first = local.Length > 0 ? local[0].ToString() : "";
        return $"{first}***@{domain}";
    }
}

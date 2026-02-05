using InfernalHierarchy.Core.Interfaces;
using InfernalHierarchy.Tools.Notifications;
using InfernalHierarchy.Tools.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Mail;

namespace InfernalHierarchy.Tools.Tools.Notifications;

public sealed class EmailNotificationTool : ITool
{
    private readonly ILogger<EmailNotificationTool> _logger;
    private readonly EmailNotificationOptions _options;
    private readonly IEmailSender _sender;

    public EmailNotificationTool(
        IOptions<EmailNotificationOptions> options,
        IEmailSender sender,
        ILogger<EmailNotificationTool> logger)
    {
        _options = options.Value;
        _sender = sender;
        _logger = logger;
    }

    public string Name => "email_send";

    public string Description => "Send an email notification via SMTP. Params: to, subject, body (optional: is_html, cc, bcc, reply_to).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return new ToolResult
            {
                Success = false,
                Output = string.Empty,
                Error = "Email notifications are disabled (Email:Enabled=false)"
            };
        }

        var to = GetString(parameters, "to");
        var subject = GetString(parameters, "subject");
        var body = GetString(parameters, "body");

        if (string.IsNullOrWhiteSpace(to))
        {
            return Fail("Missing required parameter: to");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Fail("Missing required parameter: subject");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Fail("Missing required parameter: body");
        }

        var isHtml = GetBool(parameters, "is_html") ?? false;

        using var message = new MailMessage();

        try
        {
            message.From = new MailAddress(
                _options.FromAddress,
                string.IsNullOrWhiteSpace(_options.FromName) ? null : _options.FromName);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Invalid FromAddress configured");
            return Fail("Email tool misconfigured: invalid Email:FromAddress");
        }

        if (!TryAddAddresses(message.To, to, out var toError))
        {
            return Fail($"Invalid 'to' address list: {toError}");
        }

        var cc = GetString(parameters, "cc");
        if (!string.IsNullOrWhiteSpace(cc) && !TryAddAddresses(message.CC, cc, out var ccError))
        {
            return Fail($"Invalid 'cc' address list: {ccError}");
        }

        var bcc = GetString(parameters, "bcc");
        if (!string.IsNullOrWhiteSpace(bcc) && !TryAddAddresses(message.Bcc, bcc, out var bccError))
        {
            return Fail($"Invalid 'bcc' address list: {bccError}");
        }

        var replyTo = GetString(parameters, "reply_to");
        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            try
            {
                message.ReplyToList.Add(new MailAddress(replyTo));
            }
            catch (FormatException)
            {
                return Fail("Invalid reply_to email address");
            }
        }

        message.Subject = subject;
        message.Body = body;
        message.IsBodyHtml = isHtml;

        try
        {
            await _sender.SendAsync(message, ct).ConfigureAwait(false);
            return new ToolResult { Success = true, Output = "Email sent" };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email send failed");
            return new ToolResult
            {
                Success = false,
                Output = string.Empty,
                Error = ex.Message
            };
        }
    }

    private static ToolResult Fail(string message) => new()
    {
        Success = false,
        Output = string.Empty,
        Error = message
    };

    private static string? GetString(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            _ => value.ToString()
        };
    }

    private static bool? GetBool(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is string s && bool.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryAddAddresses(MailAddressCollection target, string raw, out string? error)
    {
        error = null;

        var parts = raw
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            error = "No addresses provided";
            return false;
        }

        foreach (var part in parts)
        {
            try
            {
                target.Add(new MailAddress(part));
            }
            catch (FormatException)
            {
                error = $"Invalid email address: {part}";
                return false;
            }
        }

        return true;
    }
}

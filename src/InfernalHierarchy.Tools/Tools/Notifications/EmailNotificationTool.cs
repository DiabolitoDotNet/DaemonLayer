using InfernalHierarchy.Tools.Notifications;
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

    public string Description => "Send an email notification via SMTP. Params: to, subject, body (optional: is_html, cc, bcc, reply_to). Aliases accepted: recipient->to, message/content/text->body, title->subject.";

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

        var to = GetFirstString(parameters, "to", "recipient", "email", "address", "to_email", "toAddress", "to_address");
        if (IsPlaceholderEmail(to))
        {
            to = null;
        }

        var subject = GetFirstString(parameters, "subject", "subjeect", "title");
        var body = GetFirstString(parameters, "body", "message", "content", "text");

        if (string.IsNullOrWhiteSpace(to))
        {
            to = _options.DefaultTo;
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            return Fail("Missing required parameter: to (and Email:DefaultTo is not configured)");
        }

        // Some transports inject non-email correlation ids (e.g., http request ids). If so, fall back to DefaultTo.
        if (LooksLikeHttpCorrelationId(to) || !IsValidAddressList(to))
        {
            if (!string.IsNullOrWhiteSpace(_options.DefaultTo))
            {
                _logger.LogDebug("Invalid recipient '{To}', falling back to Email:DefaultTo", to);
                to = _options.DefaultTo;
            }
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            return Fail("Missing required parameter: to (and Email:DefaultTo is not configured)");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Fail("Missing required parameter: subject");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Fail("Missing required parameter: body");
        }

        if (LooksLikePlaceholder(body))
        {
            return Fail("Email body looks like a placeholder (e.g., '<insert ...>'). Include real content and retry.");
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

    private static string? GetFirstString(Dictionary<string, object> parameters, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetString(parameters, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsPlaceholderEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value.Trim();
        return v.Equals("USER_EMAIL", StringComparison.OrdinalIgnoreCase)
            || v.Equals("YOUR_EMAIL", StringComparison.OrdinalIgnoreCase)
            || v.Equals("RECIPIENT_EMAIL", StringComparison.OrdinalIgnoreCase)
            || v.Equals("<USER_EMAIL>", StringComparison.OrdinalIgnoreCase)
            || v.Equals("<YOUR_EMAIL>", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHttpCorrelationId(string value)
        => value.Trim().StartsWith("http-", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidAddressList(string raw)
    {
        var parts = raw
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            try
            {
                _ = new MailAddress(part);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikePlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value.Trim();
        return v.Contains("<insert", StringComparison.OrdinalIgnoreCase)
            || v.Contains("TODO", StringComparison.OrdinalIgnoreCase)
            || v.Contains("<placeholder", StringComparison.OrdinalIgnoreCase)
            || v.Contains("${", StringComparison.Ordinal)
            || v.Contains("{{", StringComparison.Ordinal)
            || v.Contains("}}", StringComparison.Ordinal);
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

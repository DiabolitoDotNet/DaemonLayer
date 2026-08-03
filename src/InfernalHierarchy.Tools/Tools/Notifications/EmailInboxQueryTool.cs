using System.Text.Json;
using InfernalHierarchy.Core.Serialization;
using InfernalHierarchy.Tools.Notifications;
using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Tools.Tools.Notifications;

public sealed class EmailInboxQueryTool : ITool
{
    private readonly EmailInboxQueryOptions _options;
    private readonly IEmailInboxQueryClient _client;
    private readonly ILogger<EmailInboxQueryTool> _logger;

    public EmailInboxQueryTool(
        IOptions<EmailInboxQueryOptions> options,
        IEmailInboxQueryClient client,
        ILogger<EmailInboxQueryTool> logger)
    {
        _options = options.Value;
        _client = client;
        _logger = logger;
    }

    public string Name => "email_inbox_query";

    public string Description =>
        "Read-only mailbox query via IMAP. Params: from (optional), subject (optional), since (optional ISO date), unread_only (bool, optional), max_results (int, optional).";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return new ToolResult
            {
                Success = false,
                Error = "Inbox query is disabled (EmailInbox:Enabled=false)",
                Output = string.Empty
            };
        }

        if (ContainsCredentialInjection(parameters))
        {
            return new ToolResult
            {
                Success = false,
                Error = "Credentials must come from secure configuration references, not tool parameters.",
                Output = string.Empty
            };
        }

        if (string.IsNullOrWhiteSpace(_options.Host)
            || string.IsNullOrWhiteSpace(_options.Username)
            || string.IsNullOrWhiteSpace(_options.Password))
        {
            return new ToolResult
            {
                Success = false,
                Error = "EmailInbox configuration is incomplete (Host/Username/Password required).",
                Output = string.Empty
            };
        }

        var fromFilter = GetString(parameters, "from", "sender", "from_email");
        var subjectFilter = GetString(parameters, "subject", "subject_contains", "sujet");
        var since = GetDate(parameters, "since", "since_utc", "after");
        var unreadOnly = GetBool(parameters, "unread_only", "unread") ?? false;
        var maxResults = GetInt(parameters, "max_results", "limit") ?? _options.MaxResults;
        maxResults = Math.Clamp(maxResults, 1, 100);

        var request = new EmailInboxQueryRequest(
            FromFilter: fromFilter,
            SubjectFilter: subjectFilter,
            SinceUtc: since,
            UnreadOnly: unreadOnly,
            MaxResults: maxResults);

        try
        {
            var messages = await _client.QueryAsync(_options, request, ct).ConfigureAwait(false);
            var serializedMessages = new object[messages.Count];
            for (var i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                serializedMessages[i] = new
                {
                    id = m.Id,
                    from = m.From,
                    subject = m.Subject,
                    date_utc = m.DateUtc,
                    unread = m.Unread
                };
            }

            var payload = JsonSerializer.Serialize(new
            {
                count = messages.Count,
                filters = new
                {
                    from = fromFilter,
                    subject = subjectFilter,
                    since_utc = since,
                    unread_only = unreadOnly,
                    max_results = maxResults
                },
                messages = serializedMessages
            }, JsonDefaults.WebIndented);

            return new ToolResult
            {
                Success = true,
                Output = payload,
                Metadata =
                {
                    ["message_count"] = messages.Count,
                    ["read_only"] = true,
                    ["provider"] = "imap"
                }
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inbox query failed");
            return new ToolResult
            {
                Success = false,
                Error = ex.Message,
                Output = string.Empty
            };
        }
    }

    private static bool ContainsCredentialInjection(Dictionary<string, object> parameters)
    {
        foreach (var key in parameters.Keys)
        {
            if (key.Contains("password", StringComparison.OrdinalIgnoreCase)
                || key.Contains("pwd", StringComparison.OrdinalIgnoreCase)
                || key.Contains("username", StringComparison.OrdinalIgnoreCase)
                || key.Contains("login", StringComparison.OrdinalIgnoreCase)
                || key.Contains("host", StringComparison.OrdinalIgnoreCase)
                || key.Contains("port", StringComparison.OrdinalIgnoreCase)
                || key.Contains("smtp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetString(Dictionary<string, object> parameters, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var value) || value is null)
            {
                continue;
            }

            var stringValue = value.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetDate(Dictionary<string, object> parameters, params string[] keys)
    {
        var raw = GetString(parameters, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static bool? GetBool(Dictionary<string, object> parameters, params string[] keys)
    {
        var raw = GetString(parameters, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        if (raw == "1")
        {
            return true;
        }

        if (raw == "0")
        {
            return false;
        }

        return null;
    }

    private static int? GetInt(Dictionary<string, object> parameters, params string[] keys)
    {
        var raw = GetString(parameters, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, out var parsed) ? parsed : null;
    }
}

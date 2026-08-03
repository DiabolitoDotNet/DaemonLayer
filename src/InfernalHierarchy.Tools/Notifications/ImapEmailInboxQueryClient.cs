using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Tools.Notifications;

public sealed class ImapEmailInboxQueryClient : IEmailInboxQueryClient
{
    private readonly ILogger<ImapEmailInboxQueryClient> _logger;

    public ImapEmailInboxQueryClient(ILogger<ImapEmailInboxQueryClient>? logger = null)
    {
        _logger = logger ?? NullLogger<ImapEmailInboxQueryClient>.Instance;
    }

    public async Task<IReadOnlyList<EmailInboxMessageSummary>> QueryAsync(
        EmailInboxQueryOptions options,
        EmailInboxQueryRequest request,
        CancellationToken ct = default)
    {
        using var client = new ImapClient();
        client.Timeout = options.TimeoutMs;

        await client.ConnectAsync(options.Host, options.Port, options.UseSsl, ct).ConfigureAwait(false);
        await client.AuthenticateAsync(options.Username, options.Password, ct).ConfigureAwait(false);

        var folder = await client.GetFolderAsync(options.Folder, ct).ConfigureAwait(false);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);

        var query = SearchQuery.All;

        if (!string.IsNullOrWhiteSpace(request.FromFilter))
        {
            query = query.And(SearchQuery.FromContains(request.FromFilter));
        }

        if (!string.IsNullOrWhiteSpace(request.SubjectFilter))
        {
            query = query.And(SearchQuery.SubjectContains(request.SubjectFilter));
        }

        if (request.SinceUtc.HasValue)
        {
            query = query.And(SearchQuery.DeliveredAfter(request.SinceUtc.Value.UtcDateTime));
        }

        if (request.UnreadOnly)
        {
            query = query.And(SearchQuery.NotSeen);
        }

        var uids = await folder.SearchAsync(query, ct).ConfigureAwait(false);
        var limited = uids
            .OrderByDescending(uid => uid.Id)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();

        if (limited.Count == 0)
        {
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);
            _logger.LogInformation("Inbox query completed | matched=0");
            return Array.Empty<EmailInboxMessageSummary>();
        }

        var fetched = await folder.FetchAsync(
                limited,
                MessageSummaryItems.Envelope | MessageSummaryItems.Flags,
                ct)
            .ConfigureAwait(false);

        var summaries = new List<EmailInboxMessageSummary>(limited.Count);

        foreach (var item in fetched.OrderByDescending(x => x.UniqueId.Id))
        {
            if (item?.Envelope is null)
            {
                continue;
            }

            var from = item.Envelope.From?.Mailboxes?.FirstOrDefault();
            var fromText = from is null
                ? string.Empty
                : string.IsNullOrWhiteSpace(from.Name)
                    ? from.Address
                    : $"{from.Name} <{from.Address}>";

            var subject = item.Envelope.Subject ?? string.Empty;
            var date = item.Envelope.Date?.ToUniversalTime() ?? DateTimeOffset.MinValue;
            var unread = !item.Flags.HasValue || !item.Flags.Value.HasFlag(MessageFlags.Seen);

            summaries.Add(new EmailInboxMessageSummary(
                Id: item.UniqueId.Id.ToString(),
                From: fromText,
                Subject: subject,
                DateUtc: date,
                Unread: unread));
        }

        await client.DisconnectAsync(true, ct).ConfigureAwait(false);

        _logger.LogInformation("Inbox query completed | matched={Count}", summaries.Count);
        return summaries;
    }
}

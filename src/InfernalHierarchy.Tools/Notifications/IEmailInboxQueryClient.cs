using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Tools.Notifications;

public sealed record EmailInboxQueryRequest(
    string? FromFilter,
    string? SubjectFilter,
    DateTimeOffset? SinceUtc,
    bool UnreadOnly,
    int MaxResults);

public sealed record EmailInboxMessageSummary(
    string Id,
    string From,
    string Subject,
    DateTimeOffset DateUtc,
    bool Unread);

public interface IEmailInboxQueryClient
{
    Task<IReadOnlyList<EmailInboxMessageSummary>> QueryAsync(
        EmailInboxQueryOptions options,
        EmailInboxQueryRequest request,
        CancellationToken ct = default);
}

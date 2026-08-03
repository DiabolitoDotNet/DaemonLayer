using InfernalHierarchy.Tools.Notifications;
using InfernalHierarchy.Tools.Options;

namespace InfernalHierarchy.Host.Tests.E2E;

public sealed class FakeEmailInboxQueryClient : IEmailInboxQueryClient
{
    public List<EmailInboxQueryRequest> Calls { get; } = new();

    public Task<IReadOnlyList<EmailInboxMessageSummary>> QueryAsync(
        EmailInboxQueryOptions options,
        EmailInboxQueryRequest request,
        CancellationToken ct = default)
    {
        Calls.Add(request);

        var messages = new List<EmailInboxMessageSummary>
        {
            new(
                Id: "m-001",
                From: "Alerts <alerts@example.com>",
                Subject: "Build status",
                DateUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                Unread: true),
            new(
                Id: "m-002",
                From: "Ops <ops@example.com>",
                Subject: "Nightly report",
                DateUtc: DateTimeOffset.UtcNow.AddMinutes(-15),
                Unread: false)
        };

        return Task.FromResult<IReadOnlyList<EmailInboxMessageSummary>>(messages);
    }
}

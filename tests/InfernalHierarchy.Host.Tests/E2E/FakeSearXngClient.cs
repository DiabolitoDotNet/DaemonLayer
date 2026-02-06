using InfernalHierarchy.Tools.Clients.Search;

namespace InfernalHierarchy.Host.Tests.E2E;

public sealed class FakeSearXngClient : ISearXngClient
{
    public List<(string Query, int Count)> Calls { get; } = new();

    public Task<WebSearchResponse> SearchAsync(string query, int count, CancellationToken ct = default)
    {
        Calls.Add((query, count));

        var results = new List<WebSearchResultItem>
        {
            new(
                Title: "Example Result 1",
                Url: "https://example.test/1",
                Snippet: "Snippet 1"),
            new(
                Title: "Example Result 2",
                Url: "https://example.test/2",
                Snippet: "Snippet 2")
        };

        return Task.FromResult(new WebSearchResponse(results));
    }
}

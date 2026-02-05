namespace InfernalHierarchy.Tools.Clients.Search;

public interface IBraveSearchClient
{
    Task<WebSearchResponse> SearchAsync(string query, int count, CancellationToken ct = default);
}

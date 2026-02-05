namespace InfernalHierarchy.Tools.Clients.Search;

public interface ISearXngClient
{
    Task<WebSearchResponse> SearchAsync(string query, int count, CancellationToken ct = default);
}

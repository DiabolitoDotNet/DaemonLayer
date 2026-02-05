namespace InfernalHierarchy.Tools.Clients.Search;

public sealed record WebSearchResponse(
    IReadOnlyList<WebSearchResultItem> Results,
    string? Error = null);

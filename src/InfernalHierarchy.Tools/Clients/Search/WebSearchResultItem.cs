using System.Diagnostics.CodeAnalysis;

namespace InfernalHierarchy.Tools.Clients.Search;

[SuppressMessage(
    "Design",
    "CA1056:Uri properties should not be strings",
    Justification = "Search providers may return relative or malformed URL strings; preserving raw provider output avoids parse-loss and keeps troubleshooting fidelity.")]
public sealed record WebSearchResultItem(
    string Title,
    string Url,
    string Snippet);

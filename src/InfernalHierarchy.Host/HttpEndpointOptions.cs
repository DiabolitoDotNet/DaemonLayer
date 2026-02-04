namespace InfernalHierarchy.Host;

/// <summary>
/// HTTP endpoint configuration for the worker host.
/// </summary>
public sealed class HttpEndpointOptions
{
    /// <summary>
    /// Whether the HTTP server is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Semicolon-separated URLs for Kestrel to bind to (e.g. "http://localhost:5080").
    /// </summary>
    public string Urls { get; set; } = "http://localhost:5080";
}

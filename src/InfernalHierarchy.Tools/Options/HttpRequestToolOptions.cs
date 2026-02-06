namespace InfernalHierarchy.Tools.Options;

public sealed class HttpRequestToolOptions
{
    public bool Enabled { get; set; } = false;

    public int TimeoutMs { get; set; } = 15_000;
    public int MaxResponseBytes { get; set; } = 512_000;

    public bool AllowHttpOnLocalhost { get; set; } = true;

    public List<string> AllowedMethods { get; set; } = new() { "GET", "POST" };

    /// <summary>
    /// Allowlist of hosts/domains. Supports exact match and subdomains.
    /// Example: "api.github.com" allows only api.github.com.
    /// Example: ".example.com" allows any subdomain of example.com.
    /// </summary>
    public List<string> AllowedHosts { get; set; } = new() { "localhost", "127.0.0.1" };
}

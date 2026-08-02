namespace InfernalHierarchy.Tools.Options;

public sealed class GraphQlToolOptions
{
    public bool Enabled { get; set; } = false;

    public int TimeoutMs { get; set; } = 15_000;

    public int MaxResponseBytes { get; set; } = 512_000;

    public bool AllowHttpOnLocalhost { get; set; } = true;

    public bool RequireReadOnly { get; set; } = true;

    public bool AllowIntrospection { get; set; } = false;

    public List<string> AllowedHosts { get; set; } = new() { "localhost", "127.0.0.1" };
}
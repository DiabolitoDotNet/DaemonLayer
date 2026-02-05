namespace InfernalHierarchy.Tools.Options;

public sealed class SearXNGOptions
{
    public Uri BaseUrl { get; set; } = new Uri("http://localhost:8080");
    public bool Enabled { get; set; } = true;
}

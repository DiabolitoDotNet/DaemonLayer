namespace InfernalHierarchy.Tools.Options;

public sealed class VisionToolOptions
{
    public bool Enabled { get; set; } = false;

    public string RootDirectory { get; set; } = "data/vision";

    public List<string> AllowedExtensions { get; set; } = new() { ".png", ".jpg", ".jpeg", ".webp" };

    public long MaxInputBytes { get; set; } = 10 * 1024 * 1024;

    public int TimeoutMs { get; set; } = 90_000;

    public int MaxOutputChars { get; set; } = 20_000;

    public string DefaultPrompt { get; set; } = "Describe the image, identify key elements, and answer the user question precisely.";

    public string DefaultModel { get; set; } = string.Empty;
}
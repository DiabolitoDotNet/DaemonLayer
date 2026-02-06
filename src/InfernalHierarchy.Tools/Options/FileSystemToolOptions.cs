namespace InfernalHierarchy.Tools.Options;

public sealed class FileSystemToolOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Root directory that all filesystem operations are sandboxed to.
    /// Can be absolute, or relative to the Host content root.
    /// </summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>
    /// If false, write operations are rejected even when Enabled=true.
    /// </summary>
    public bool AllowWrite { get; set; } = false;

    public int MaxReadBytes { get; set; } = 128_000;
    public int MaxWriteBytes { get; set; } = 256_000;

    public int MaxSearchFileBytes { get; set; } = 256_000;
    public int MaxSearchResults { get; set; } = 25;
    public int MaxSearchFilesScanned { get; set; } = 1_000;

    /// <summary>
    /// Optional allowlist of file extensions (e.g., .md, .txt).
    /// If empty, all extensions are allowed (not recommended).
    /// </summary>
    public List<string> AllowedExtensions { get; set; } = new()
    {
        ".txt",
        ".md",
        ".json",
        ".yaml",
        ".yml",
        ".cs",
        ".csproj",
        ".sln",
        ".props",
        ".targets"
    };
}

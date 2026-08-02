namespace InfernalHierarchy.Host.Configuration;

public sealed class ExecutionProfilesOptions
{
    public bool Enabled { get; set; } = true;

    public string DefaultProfile { get; set; } = "Research";

    public Dictionary<string, ExecutionProfilePolicy> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ExecutionProfilePolicy
{
    public bool Enabled { get; set; } = true;

    public List<string> AllowedTools { get; set; } = new();

    public List<string> DeniedTools { get; set; } = new();

    // P2.2 placeholders: currently informational, not enforced at tool auth layer yet.
    public List<string> AllowedFileScopes { get; set; } = new();

    public List<string> AllowedNetworkScopes { get; set; } = new();

    public List<string> CommandAllowlist { get; set; } = new();
}

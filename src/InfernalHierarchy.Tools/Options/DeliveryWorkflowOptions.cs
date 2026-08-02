namespace InfernalHierarchy.Tools.Options;

public sealed class DeliveryWorkflowOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Root directory for workflow execution and repository analysis.
    /// Can be absolute or relative to the host content root.
    /// </summary>
    public string RootDirectory { get; set; } = string.Empty;

    public int DefaultTimeoutMs { get; set; } = 120_000;
    public int MaxOutputBytes { get; set; } = 512_000;
    public int MaxDiscoveryFiles { get; set; } = 5_000;

    public string PackageOutputDirectory { get; set; } = "artifacts";

    /// <summary>
    /// Controlled deployment adapters keyed by adapter id.
    /// </summary>
    public Dictionary<string, DeliveryAdapterOptions> Adapters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DeliveryAdapterOptions
{
    public bool Enabled { get; set; } = false;

    public string WorkingDirectory { get; set; } = ".";

    public List<string> AllowedEnvironments { get; set; } = new();

    public string DeployExecutable { get; set; } = string.Empty;
    public List<string> DeployArguments { get; set; } = new();

    public string RollbackExecutable { get; set; } = string.Empty;
    public List<string> RollbackArguments { get; set; } = new();
}
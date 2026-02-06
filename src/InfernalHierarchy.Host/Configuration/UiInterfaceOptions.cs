namespace InfernalHierarchy.Host.Configuration;

/// <summary>
/// Configuration for the built-in web UI (served under /ui).
/// </summary>
public sealed class UiInterfaceOptions
{
    /// <summary>
    /// Whether the UI endpoints are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// If true, only requests from loopback are allowed.
    /// </summary>
    public bool LocalOnly { get; set; } = true;
}

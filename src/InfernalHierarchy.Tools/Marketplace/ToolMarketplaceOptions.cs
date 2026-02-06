using System.Collections.ObjectModel;

namespace InfernalHierarchy.Tools.Marketplace;

public sealed class ToolMarketplaceOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Directory where plugin .dll files are located.
    /// Can be absolute, or relative to the Host content root.
    /// </summary>
    public string PluginsDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Allowlist of plugin file names (e.g., "MyPlugin.dll").
    /// When marketplace is enabled, this must be non-empty to avoid accidentally loading arbitrary DLLs.
    /// </summary>
    public Collection<string> AllowedPluginFiles { get; } = new();

    public int MaxPluginBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Poll interval for discovering new/updated plugin files.
    /// </summary>
    public int RescanIntervalSeconds { get; set; } = 10;
}

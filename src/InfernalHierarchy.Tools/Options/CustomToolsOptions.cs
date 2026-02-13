using System.Collections.ObjectModel;

namespace InfernalHierarchy.Tools.Options;

/// <summary>
/// Policy controls for dynamically created tools.
/// </summary>
public sealed class CustomToolsOptions
{
    /// <summary>
    /// Enables custom tool persistence/reload and creation.
    /// When disabled, <c>create_custom_tool</c> will refuse to create tools.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Allowlist of custom tool ids approved by an operator.
    /// Used to permit tools that reference risky APIs (IO/network/process) when policy flags them.
    /// </summary>
    public Collection<string> ApprovedToolIds { get; } = new();

    /// <summary>
    /// Allowlist of custom tool names approved by an operator.
    /// Convenience alternative to <see cref="ApprovedToolIds"/>.
    /// </summary>
    public Collection<string> ApprovedToolNames { get; } = new();

    /// <summary>
    /// When true, loads/executes tools even if policy detects risky APIs.
    /// Default is false.
    /// </summary>
    public bool AllowUnsafeWithoutManualApproval { get; set; }

    /// <summary>
    /// When true, tools that are only flagged for network usage (e.g. HttpClient/System.Net)
    /// do not require manual approval.
    /// This keeps the "manual approval" guardrail for more dangerous categories (IO/process/etc)
    /// while allowing API-integration tools to be created and used without operator intervention.
    /// Default is true.
    /// </summary>
    public bool AllowNetworkWithoutManualApproval { get; set; } = true;
}

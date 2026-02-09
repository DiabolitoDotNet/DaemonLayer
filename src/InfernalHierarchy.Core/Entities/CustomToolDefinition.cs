using System.Text.Json.Serialization;

namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Persisted definition of a dynamically created tool (source-based).
/// Stored in LiteDB and recompiled on startup when permitted by policy.
/// </summary>
public sealed class CustomToolDefinition
{
    /// <summary>
    /// Stable identifier for approval/auditing.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>
    /// Tool name exposed to agents (should be stable and typically prefixed with "custom_").
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Full C# source code for the tool.
    /// </summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>
    /// Agent id that requested creation.
    /// </summary>
    public string CreatedByAgentId { get; set; } = string.Empty;

    /// <summary>
    /// Agent name that requested creation.
    /// </summary>
    public string CreatedByAgentName { get; set; } = string.Empty;

    /// <summary>
    /// UTC creation time.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// True when policy indicates the tool references risky APIs and requires manual approval.
    /// </summary>
    public bool RequiresManualApproval { get; set; }

    /// <summary>
    /// Optional short hash of the source for audit/immutability.
    /// </summary>
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>
    /// Last compilation attempt timestamp.
    /// </summary>
    public DateTimeOffset? LastCompiledAt { get; set; }

    /// <summary>
    /// Last compilation error summary (if any).
    /// </summary>
    public string? LastCompileError { get; set; }

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(ToolName) && !string.IsNullOrWhiteSpace(SourceCode);
}

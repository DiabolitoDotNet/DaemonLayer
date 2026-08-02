namespace InfernalHierarchy.Core.Entities;

/// <summary>
/// Request for temporary or baseline skill assignment.
/// </summary>
public sealed class SkillAssignmentRequest
{
    public string SkillPackId { get; init; } = string.Empty;

    public string RequestorAgentId { get; init; } = string.Empty;

    public AgentRank RequestorRank { get; init; }

    public string TargetAgentId { get; init; } = string.Empty;

    public AgentRank TargetAgentRank { get; init; }

    public bool Temporary { get; init; } = true;

    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Result of a skill assignment decision.
/// </summary>
public sealed class SkillAssignmentDecision
{
    public bool IsApproved { get; init; }

    public bool RequiresEscalation { get; init; }

    public string ReasonCode { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public static SkillAssignmentDecision Approved(string reasonCode, string message)
        => new()
        {
            IsApproved = true,
            RequiresEscalation = false,
            ReasonCode = reasonCode,
            Message = message
        };

    public static SkillAssignmentDecision Denied(string reasonCode, string message)
        => new()
        {
            IsApproved = false,
            RequiresEscalation = false,
            ReasonCode = reasonCode,
            Message = message
        };

    public static SkillAssignmentDecision EscalationRequired(string reasonCode, string message)
        => new()
        {
            IsApproved = false,
            RequiresEscalation = true,
            ReasonCode = reasonCode,
            Message = message
        };
}

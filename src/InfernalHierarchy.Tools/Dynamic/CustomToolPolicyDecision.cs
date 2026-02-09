namespace InfernalHierarchy.Tools.Dynamic;

public sealed record CustomToolPolicyDecision(
    bool Allowed,
    bool RequiresManualApproval,
    string Reason,
    IReadOnlyList<string> MatchedRules);

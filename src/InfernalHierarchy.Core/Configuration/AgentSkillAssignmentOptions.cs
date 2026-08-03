namespace InfernalHierarchy.Core.Configuration;

using System.Diagnostics.CodeAnalysis;

public sealed class AgentSkillAssignmentOptions
{
    public bool Enabled { get; set; } = true;

    public bool AutoAssignBaseSkills { get; set; } = true;

    // When true, escalation-required skill requests are automatically approved by the main agent.
    public bool AutoApproveEscalationsByMainAgent { get; set; } = true;

    public string MainAgentId { get; set; } = "lucifer";

    public bool AllowSelfServiceSkillRequests { get; set; } = true;

    public string EscalateRiskLevelAtOrAbove { get; set; } = "High";

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Configuration binding model; arrays keep rank lists and defaults compact.")]
    public string[] SelfServiceAllowedRanks { get; set; } = ["Supreme", "Prince", "Duke"];

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Configuration binding model; arrays keep defaults compact.")]
    public string[] SupremeBaseSkillPacks { get; set; } = ["core-orchestrator"];

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Configuration binding model; arrays keep defaults compact.")]
    public string[] PrinceBaseSkillPacks { get; set; } = ["team-coordination"];

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Configuration binding model; arrays keep defaults compact.")]
    public string[] DukeBaseSkillPacks { get; set; } = ["implementation-engineering"];

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Configuration binding model; arrays keep defaults compact.")]
    public string[] WorkerBaseSkillPacks { get; set; } = ["execution-worker"];

    public bool PersistRuntimeGrants { get; set; } = true;

    public string RuntimeGrantDatabasePath { get; set; } = "data/agent-skill-runtime.db";

    public int RuntimeGrantMaxEntries { get; set; } = 20000;
}

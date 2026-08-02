namespace InfernalHierarchy.Core.Configuration;

public sealed class AgentSkillAssignmentOptions
{
    public bool Enabled { get; set; } = true;

    public bool AutoAssignBaseSkills { get; set; } = true;

    // When true, escalation-required skill requests are automatically approved by the main agent.
    public bool AutoApproveEscalationsByMainAgent { get; set; } = true;

    public string MainAgentId { get; set; } = "lucifer";

    public bool AllowSelfServiceSkillRequests { get; set; } = true;

    public string EscalateRiskLevelAtOrAbove { get; set; } = "High";

    public string[] SelfServiceAllowedRanks { get; set; } = ["Supreme", "Prince", "Duke"];

    public string[] SupremeBaseSkillPacks { get; set; } = ["core-orchestrator"];

    public string[] PrinceBaseSkillPacks { get; set; } = ["team-coordination"];

    public string[] DukeBaseSkillPacks { get; set; } = ["implementation-engineering"];

    public string[] WorkerBaseSkillPacks { get; set; } = ["execution-worker"];
}

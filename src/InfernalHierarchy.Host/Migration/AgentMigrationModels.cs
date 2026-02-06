using System.Text.Json.Serialization;

namespace InfernalHierarchy.Host.Migration;

internal sealed record AgentMigrationBundle(
    string FormatVersion,
    string BundleId,
    DateTimeOffset ExportedAtUtc,
    AgentMigrationSource Source,
    string PersonaName,
    string PersonaJson,
    string AgentRank,
    IReadOnlyList<AgentMigrationFact> Facts,
    IReadOnlyList<AgentMigrationTask> Tasks,
    IReadOnlyList<AgentMigrationDecision> Decisions,
    AgentMigrationSignature? Signature);

internal sealed record AgentMigrationSource(
    string AgentId,
    string AgentName,
    string? ParentAgentId);

internal sealed record AgentMigrationFact(
    string Category,
    string Content,
    string Source,
    double Confidence,
    string Visibility,
    string? MinimumRankToView,
    IReadOnlyList<string> SharedWithAgents);

internal sealed record AgentMigrationTask(
    string Description,
    string Status,
    string? Result);

internal sealed record AgentMigrationDecision(
    string Context,
    string Action,
    string Reasoning,
    string? Outcome);

internal sealed record AgentMigrationSignature(
    string Algorithm,
    string Value);

internal sealed record AgentImportRequest(
    string BundleJson,
    string? PersonaNameOverride,
    string? ParentAgentId,
    string? AgentRankOverride,
    bool StartAgent,
    bool ImportFacts,
    bool ImportTasks,
    bool ImportDecisions,
    bool OverwritePersona);

internal sealed record AgentImportResponse(
    string AgentId,
    string PersonaName,
    string AgentRank,
    int ImportedFacts,
    int ImportedTasks,
    int ImportedDecisions);

# Analyzer Policy (Dev + CI)

## Goal

Keep local builds behaviorally aligned with CI for analyzer-driven quality gates.

## Default behavior

`Directory.Build.props` enables analyzer execution by default:

- `RunAnalyzersDuringBuild=true` (default when not explicitly overridden)
- Critical warning set is promoted to errors when CI (or local strict run) sets `EnforceCriticalWarningsAsErrors=true`

## Recommended commands

Strict parity build (same intent as CI):

```powershell
dotnet build InfernalHierarchy.sln -c Release --no-restore -p:RunAnalyzersDuringBuild=true -p:EnforceCriticalWarningsAsErrors=true
```

Ratchet parity build (critical + phased non-critical warnings as errors):

```powershell
dotnet build InfernalHierarchy.sln -c Release --no-restore -p:RunAnalyzersDuringBuild=true -p:EnforceCriticalWarningsAsErrors=true -p:EnforceNonCriticalWarningsAsErrors=true
```

Fast local iteration (explicit opt-out, non-gating):

```powershell
dotnet build InfernalHierarchy.sln -c Debug -p:RunAnalyzersDuringBuild=false
```

## Suppression policy

- Prefer fixing root causes over suppression.
- Any suppression must include a short, concrete justification.
- Keep suppression scope as narrow as possible (member-level over file/project-level).
- Revisit suppressions during refactors and remove obsolete entries.

Suppression inventory reference:

- `Documentation/Runbooks/Analyzer-Suppressions-Inventory.md`

# Analyzer Suppressions Inventory

Last updated: 2026-08-03
Review cadence: every release cut

## Policy

- Keep suppression scope narrow.
- Keep explicit justification on each suppression.
- Revisit and remove when code paths evolve.

## Current inventory

| Area | File | Rule(s) | Rationale |
|---|---|---|---|
| Agent base/runtime | src/InfernalHierarchy.Agents/Base/BaseAgent.cs | SuppressMessage entries | Narrow runtime-specific exceptions with inline justification |
| ReAct remediation DTO parsing | src/InfernalHierarchy.Agents/ReAct/DefaultCapabilityRemediationOrchestrator.cs | CA1812 | JSON-instantiated internal type |
| ReAct options binding | src/InfernalHierarchy.Agents/ReAct/ReActOptions.cs | SuppressMessage entries | Options/config binding shape |
| Config arrays | src/InfernalHierarchy.Core/Configuration/AgentSkillAssignmentOptions.cs | CA1819 | Configuration array binding ergonomics |
| Config arrays | src/InfernalHierarchy.Core/Configuration/MessageBusOptions.cs | CA1819 | Configuration array binding ergonomics |
| Incident response options | src/InfernalHierarchy.Host/Configuration/AutonomousIncidentResponseOptions.cs | CA1819 | Configuration array binding ergonomics |
| Autonomy readiness options | src/InfernalHierarchy.Host/Configuration/AutonomyReadinessOptions.cs | CA1819 | Configuration array binding ergonomics |
| Persona/migration filename normalization | src/InfernalHierarchy.Host/Migration/AgentMigrationService.cs | CA1308 pragma | Lowercase filename normalization is intentional |
| Persona/migration filename normalization | src/InfernalHierarchy.Host/Personas/PersonaFileStore.cs | CA1308 pragma | Lowercase filename normalization is intentional |
| Embedding fallback deterministic RNG | src/InfernalHierarchy.Memory/Embeddings/OnnxEmbeddingService.cs | CA5394 | Non-crypto deterministic fallback only |
| Telegram options arrays | src/InfernalHierarchy.Telegram/Options/TelegramOptions.cs | CA1819 | Configuration array binding ergonomics |
| Ollama DTO parsing | src/InfernalHierarchy.Tools/Clients/OllamaClient.cs | CA1812 | JSON-instantiated internal DTOs |
| Brave DTO parsing | src/InfernalHierarchy.Tools/Clients/Search/BraveSearchClient.cs | CA1812 | JSON-instantiated internal DTOs |
| SearXNG DTO parsing | src/InfernalHierarchy.Tools/Clients/Search/SearXngClient.cs | CA1812 | JSON-instantiated internal DTOs |
| Search result metadata model | src/InfernalHierarchy.Tools/Clients/Search/WebSearchResultItem.cs | SuppressMessage entries | Transport/deserialization model constraints |
| SQL readonly tool | src/InfernalHierarchy.Tools/Tools/Sql/SqlReadOnlyQueryTool.cs | SuppressMessage entries | Defensive parsing/perf constraints |

## Gate usage

Use strict build gate before merge:

```powershell
dotnet build InfernalHierarchy.sln -c Release --no-restore -p:RunAnalyzersDuringBuild=true -p:EnforceCriticalWarningsAsErrors=true
```

Any new suppression must be added to this inventory in the same change.

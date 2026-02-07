# ADR 0004: Critique loop (self-reflection) via dedicated Critic persona

- **Date:** 2026-02-07
- **Status:** Accepted
- **Deciders:** DaemonLayer maintainers
- **Tags:** agents, quality, personas, cost-control

## Context

As the system adds more tools and multi-agent delegation, a branch can produce a plausible final answer that still has:

- internal contradictions,
- missing sources / missing checks,
- an incomplete synthesis (especially after tool-heavy branches).

We want a lightweight quality-improvement step that:

- improves output quality when it matters,
- does not significantly increase token cost for routine tasks,
- does not create new runaway loops or meta-thrashing with supervision.

## Decision

Introduce an optional **Critique loop** that runs only for **Prince/Supreme** agents and only on **completed branch reports**.

- When enabled (`Critique:Enabled=true`), a Prince/Supreme agent may spawn a short-lived Critic sub-agent (persona name default: `Orobas`, rank default: `Duke`).
- The Critique loop is **gated by heuristics** to control cost:
  - branch depth ≥ `Critique:MinDepth`, OR
  - tool call count ≥ `Critique:MinToolCalls`, OR
  - explicit user request (keyword match via `Critique:TriggerKeywords`).
- The Critic persona returns **strict JSON only** (no markdown) with optional `improved_summary`.
- If `improved_summary` is present and non-empty, it replaces the branch report content; critique metadata is attached to the report payload.
- The Critic agent is **terminated** after the critique.
- The Critique loop is **skipped** for supervisor replan commands (`SUPERVISOR_REPLAN:`) to avoid critiquing meta-interventions.

## Alternatives considered

- Always run critique on every completed report
  - Pros: consistent quality checks
  - Cons: high token cost; slows normal interactions

- Critique inside the same agent (self-review)
  - Pros: no extra agent creation
  - Cons: less role separation; tends to rationalize rather than challenge; harder to enforce strict output schema

- Rely on tool-level validation only
  - Pros: cheap
  - Cons: does not catch synthesis-quality issues and contradictions in narrative outputs

- Human-only review
  - Pros: best quality
  - Cons: defeats the purpose of autonomous operation; not always available

## Consequences

### Positive

- Improves synthesis quality for deep/tool-heavy branches.
- Keeps routine cost low via heuristics and end-of-branch-only execution.
- Role separation (dedicated Critic persona) yields more reliable adversarial review.
- Provides structured critique metadata usable for later audits and iteration.

### Negative / Trade-offs

- Adds complexity to agent runtime (factory dependency + JSON parsing).
- Can still add non-trivial token cost when frequently triggered.
- Quality depends on the Critic persona prompt discipline and model behavior.

## Appendix: Critique JSON schema

The Critic persona (default: `Orobas`) must return a **single JSON object only** (no markdown) matching this schema.

Notes:

- The canonical schema uses `snake_case` keys.
- The runtime parser is tolerant and also accepts `camelCase` variants for the scalar fields.
- `improved_summary` is optional in practice: when empty/whitespace, the original branch report content is kept.

### Fields

- `quality_score` (int, 0–10): overall quality of the branch result.
- `contradictions` (string[]): contradictions or inconsistencies found.
- `missing_sources` (string[]): claims that need verification or citations.
- `recommendations` (string[]): suggested fixes or follow-ups.
- `should_rollback` (bool): recommendation only; indicates the branch result should be rolled back/revised.
- `should_kill_branch` (bool): recommendation only; indicates the branch should be abandoned due to risk/low value.
- `improved_summary` (string): improved synthesis to use as the final branch answer when safe.

### Example

```json
{
  "quality_score": 7,
  "contradictions": ["The answer claims X and later assumes not-X."],
  "missing_sources": ["No source provided for the performance claim."],
  "recommendations": ["Cite a benchmark or remove the claim.", "Clarify assumptions about input size."],
  "should_rollback": false,
  "should_kill_branch": false,
  "improved_summary": "Revised summary that removes unsupported claims and clarifies assumptions."
}
```

## Notes / Links

- Related docs:
  - [Documentation/Features.md](../Features.md)
  - [Documentation/Solution-Architecture.md](../Solution-Architecture.md)
- Related code:
  - `src/InfernalHierarchy.Agents/ReAct/ReActAgent.cs`
  - `src/InfernalHierarchy.Core/Configuration/CritiqueOptions.cs`
  - `souls/orobas.json`

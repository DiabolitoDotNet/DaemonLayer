# ADR 0006: Personas and templates are JSON assets

## Context

Agent behavior and task shaping must be editable without recompiling the host. Operators also need a durable, reviewable place for persona and template content.

## Decision

Personas and templates are stored as JSON assets under repository-managed directories:

- personas under `souls/`
- templates under `templates/`

The runtime loads these assets through dedicated loader/services instead of hardcoding them in the host.

## Consequences

Positive:

- behavior can evolve without code changes,
- personas/templates remain reviewable and versionable,
- extension workflows stay simple for new specialists and task shapes.

Trade-offs:

- schema drift must be managed carefully,
- operational docs must explain discovery and path resolution,
- malformed assets fail at runtime instead of compile time.
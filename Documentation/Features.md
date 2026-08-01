# Features Catalog

This is the consolidated feature list of the InfernalHierarchy solution. It intentionally stays at “what exists / what it does” level. For configuration details, follow the links to existing topic docs.

## Agent system

- Hierarchical agent model (Supreme → Prince → Duke → Worker)
- ReAct-style reasoning loop with tool use
- Agent lifecycle management (creation, tracking, shutdown)
- Agent supervision (`AgentSupervisor`) to detect stalls/loops and intervene (root replan, optional preemption with escalation)
- Optional self-reflection / critique loop (`Critique`) for Prince/Supreme branches via a dedicated Critic persona (default: `Orobas`), triggered only on completed branch reports (and skipped for supervisor replans)
- Persona-driven behavior (souls in `souls/`)
- Template-driven task shaping (see `templates/`)

## Tools framework

- Standard tool abstraction (`ITool`) for pluggability
- Tool registry / discovery
- Central execution pipeline (validation → authorization → optional cache → rate limit → execute → observe)
- Short-lived tool result cache (`ToolCache`) backed by LiteDB (tool name + stable input signature, TTL 5–30 minutes)
- Tool authorization policies (deny-by-default possible)
- Tool rate limiting / throttling
- Tool telemetry (timings, outcomes, correlation)

## Memory & knowledge

- Shared memory (`ISharedMemory`) backed by LiteDB
- Memory entry types for facts/decisions/tasks and general notes
- Optional vector memory (`IVectorMemory`) for semantic retrieval (when configured)
- Background retention / pruning
- (Where enabled) embeddings via ONNX-based embedding service

## Messaging & coordination

- Channel-based internal message bus (`IMessageBus`)
- Collaboration requests and agent-to-agent coordination primitives
- Tenant isolation primitives (`ITenantIsolationService`, `TenantContext`)

## Telegram interface

- Telegram bot integration for inbound/outbound messaging
- Command routing and input validation
- Bot-to-agent bridging (updates become tasks)

## Observability

- Structured logging and log enrichment
- OpenTelemetry tracing across agent runs and tool invocations
- Prometheus metrics endpoint + health checks
- Performance monitoring services

See: [OBSERVABILITY](../OBSERVABILITY.md)

## Security

- Tool authorization service with configurable permissions
- Operator-level API key support for privileged operations (when enabled)
- Secret rotation service

See: [SECURITY_CONFIG](../SECURITY_CONFIG.md)

## Operations & deployment

- Docker support (`Dockerfile`, `docker-compose.yml`)
- Environment-specific settings (`appsettings.Development.json`, `appsettings.Production.json`)
- Local secrets guidance (`secrets/` and example json)
- LiteDB backup automation with retention/rotation (`MemoryBackup`)
- Tenant-backed agent quota enforcement at creation time

## Extensibility points

- Implement new tools by adding a class implementing `ITool` and registering it in the Tools project.
- Add new personas by creating a new JSON file in `souls/` and ensuring it is discoverable by the persona loader.
- Add new background services via the Host project.
- Add new memory backends by implementing `ISharedMemory` or `IVectorMemory`.

## Extension guide

### Add a new tool

1. Implement `ITool` in the Tools project.
2. Keep the tool focused on one responsibility with explicit required parameters.
3. Register it in Host DI so `ToolRegistrationService` can discover it.
4. Add or update `ToolPermissions` defaults/config if the tool has side effects or operator sensitivity.
5. Add the tool name to the relevant persona JSON files.
6. Add at least one direct tool test and one pipeline/integration-oriented test when behavior is safety-sensitive.

### Add a new persona

1. Create a new JSON file under `souls/`.
2. Define role, prompt, specializations, and available tools explicitly.
3. Keep tool access narrow; personas should only receive tools they genuinely need.
4. If the persona is operator-facing or powerful, validate it against the security and runbook docs.

### Add a new template

1. Add the template asset under `templates/`.
2. Keep it task-shaped rather than persona-shaped.
3. Verify the template path is reachable through the configured template root.
4. Add or update docs when the template introduces a new operator workflow.

### Add a new hosted/background service

1. Register it from the Host composition root.
2. Gate optional behavior behind configuration.
3. Add logging/health/metrics expectations when the service is operationally significant.
4. Prefer one hosted service per operational responsibility.

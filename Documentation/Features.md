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

## Extensibility points

- Implement new tools by adding a class implementing `ITool` and registering it in the Tools project.
- Add new personas by creating a new JSON file in `souls/` and ensuring it is discoverable by the persona loader.
- Add new background services via the Host project.
- Add new memory backends by implementing `ISharedMemory` or `IVectorMemory`.

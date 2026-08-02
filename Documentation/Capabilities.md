# Capabilities & Use Cases

This document explains what the solution can *do* in practice and how to think about composing capabilities.

## What it is

InfernalHierarchy is an autonomous, tool-using agent system that lives inside a long-running .NET host. Telegram is the primary interface for humans; tools and shared memory are how the system interacts with the world and stays consistent over time.

## Core capabilities

### 1) Multi-agent delegation

Higher-rank agents can:

- decompose goals into tasks,
- spawn or select specialized sub-agents,
- coordinate results via shared memory and message bus events.

This enables “manager + specialist” workflows.

### 2) Tool-mediated action

Agents don’t directly perform privileged actions; they request tool executions.

Benefits:

- authorization and policy enforcement,
- rate limiting and predictable resource usage,
- centralized telemetry and auditing.

### 3) Durable memory

The system can:

- persist facts, decisions, and task outcomes,
- retrieve prior context for continuity,
- prune/retain memory to keep storage bounded.

### 4) Observability-first operation

You can:

- trace why a tool was called and how long it took,
- monitor health and performance,
- diagnose agent behavior from logs + spans + metrics.

### 5) Tenant-aware operation

A tenant context can be carried through:

- memory reads/writes,
- tool authorization checks,
- message bus events.

This enables multi-user use with isolation.

### 6) Autonomous incident response

The host continuously monitors runtime degradation signals and can apply controlled mitigations without operator intervention:

- trigger root-agent replan when timeout/rejection/stall signals spike,
- preempt a non-root looping branch when needed,
- temporarily throttle selected high-amplification tools while the system recovers.

Every mitigation is emitted as an auditable decision event and reflected in incident-response metrics.

## Example workflows

### Research + synthesis

1. Receive query via Telegram
2. Agent uses web search tool (if authorized)
3. Summarizes, cites sources (where applicable), and stores a condensed note in shared memory
4. Returns a final answer to Telegram

### Task orchestration

1. Supreme/Prince agent receives “build me a plan”
2. Decomposes into subtasks
3. Creates specialized sub-agents (e.g., web researcher, code generator)
4. Aggregates results, commits a final consolidated output

### Operational assistant

1. Telegram message requests system status
2. Host surfaces health/metrics or a summarized state
3. Operator-only actions require the operator API key and proper permissions

### Deterministic tool execution

1. Operator sends an explicit tool invocation through `/api/chat`
2. ReAct loop detects `Invoke tool <name> {json}`
3. Requested tool executes through the standard tool pipeline
4. The result is returned without relying on model interpretation

This is especially useful for:

- operator debugging,
- custom tool creation,
- reproducible incident response,
- validating permission or policy behavior.

See: [Custom Tools Runbook](Runbooks/Custom-Tools.md)

### Custom tool lifecycle

1. Operator or authorized agent requests `create_custom_tool`
2. Source is generated or templated
3. Security policy evaluates the source
4. Source is compiled, persisted, and registered when allowed
5. The resulting `custom_*` tool can then be invoked if separately authorized

If an overwrite attempt fails at compile stage, the previous persisted definition is restored automatically (safe rollback).

This gives the system a controlled way to extend its tool surface without rebuilding the host.

See: [Custom Tools Runbook](Runbooks/Custom-Tools.md)

## Capability recipes

### Add a new tool safely

1. Implement the tool and register it in the host.
2. Decide whether it is read-only or side-effecting.
3. Add authorization defaults and, if needed, side-effect dedupe expectations.
4. Add direct tests plus at least one pipeline or orchestration test.
5. Update the relevant persona/tool docs.

### Add a new specialist persona

1. Create a JSON asset in `souls/`.
2. Keep available tools narrow and role-specific.
3. Prefer composition through existing tools before inventing new ones.
4. Validate the persona in a realistic orchestration or chat flow.

### Add a new template-driven workflow

1. Add the template under `templates/`.
2. Keep task framing reusable across personas.
3. Document operator-facing usage when the template changes system behavior noticeably.

### Operate the local memory store safely

1. Monitor LiteDB health and file size.
2. Use pruning for retention control.
3. Use scheduled backups for rollback/recovery.
4. Validate visibility rules when facts are shared across ranks.

## Boundaries and control levers

- **Tool permissions** are the main safety boundary.
- **Rate limiting** controls blast radius and prevents runaway loops.
- **Tool result caching** can reduce duplicate expensive tool calls (where safe).
- **Critique loop** (Prince/Supreme, end-of-branch) can improve synthesis quality when explicitly requested or when branches get deep/tool-heavy.
- **Memory pruning** prevents infinite persistence.
- **Resource monitoring** can detect overload.

## Where to go deeper

- Architecture: [Solution Architecture](Solution-Architecture.md)
- Incident operations: [Runbooks/Incident-Memory-Bloat.md](Runbooks/Incident-Memory-Bloat.md)
- Features and extension guide: [Features Catalog](Features.md)
- Runbooks: [Documentation README](README.md)
- Security model: [SECURITY_CONFIG](../SECURITY_CONFIG.md)
- Observability model: [OBSERVABILITY](../OBSERVABILITY.md)
- Advanced/experimental capabilities: [ADVANCED_FEATURES](../ADVANCED_FEATURES.md)

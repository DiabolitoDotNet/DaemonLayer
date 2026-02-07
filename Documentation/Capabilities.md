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

## Boundaries and control levers

- **Tool permissions** are the main safety boundary.
- **Rate limiting** controls blast radius and prevents runaway loops.
- **Tool result caching** can reduce duplicate expensive tool calls (where safe).
- **Critique loop** (Prince/Supreme, end-of-branch) can improve synthesis quality when explicitly requested or when branches get deep/tool-heavy.
- **Memory pruning** prevents infinite persistence.
- **Resource monitoring** can detect overload.

## Where to go deeper

- Architecture: [Solution Architecture](Solution-Architecture.md)
- Security model: [SECURITY_CONFIG](../SECURITY_CONFIG.md)
- Observability model: [OBSERVABILITY](../OBSERVABILITY.md)
- Advanced/experimental capabilities: [ADVANCED_FEATURES](../ADVANCED_FEATURES.md)

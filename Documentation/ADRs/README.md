# Architecture Decision Records (ADRs)

This folder captures key architectural decisions in a short, durable format.

## How to use

- One decision per file, numbered sequentially.
- Keep them short and factual: context → decision → consequences.
- When decisions change, create a new ADR that supersedes the old one; don’t rewrite history.

## Index

- [0001 - Local-first runtime with Telegram + Ollama](0001-local-first-telegram-ollama.md)
- [0002 - Channel-based message bus for inter-agent communication](0002-channel-message-bus.md)
- [0003 - LiteDB-backed shared memory with pruning/retention](0003-litedb-shared-memory.md)
- [0004 - Critique loop (self-reflection) via dedicated Critic persona](0004-critique-loop-self-reflection.md)
- [0005 - Tool execution pipeline as the security boundary](0005-tool-execution-pipeline-security-boundary.md)
- [0006 - Personas and templates as JSON assets](0006-personas-and-templates-as-json-assets.md)
- [TEMPLATE](TEMPLATE.md)

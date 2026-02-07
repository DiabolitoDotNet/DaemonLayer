# ADR 0002: Channel-based message bus for inter-agent communication

- **Date:** 2026-02-06
- **Status:** Accepted
- **Deciders:** Project maintainers
- **Tags:** messaging, channels, backpressure, reliability

## Context

Agents and host components need to communicate without tight coupling. The system is long-running and must remain responsive under load, avoid unbounded queues, and support graceful shutdown.

Key forces:

- Multiple producers and consumers (agents, background services, Telegram router).
- Avoid blocking threads or creating per-consumer bespoke queues.
- Provide backpressure to prevent runaway memory growth.
- Maintain good observability (message flows should be traceable).

## Decision

Use a Channel-based message bus (via `System.Threading.Channels`) behind the `IMessageBus` abstraction.

- Publish/subscribe semantics are represented as async streams (`IAsyncEnumerable`).
- Targeted messages are keyed by agent id; broadcasts are handled separately.

## Alternatives considered

- **In-process event aggregator (synchronous)**
  - Pros: simple.
  - Cons: can block publishers, harder to apply backpressure, harder to isolate slow consumers.

- **External broker (RabbitMQ/Kafka/Azure Service Bus)**
  - Pros: scalable, durable.
  - Cons: violates local-first bias; adds operational overhead and failure modes.

- **In-memory concurrent queue per agent**
  - Pros: straightforward.
  - Cons: hard to manage lifecycle/cleanup; no built-in backpressure; risk of unbounded growth.

## Consequences

### Positive

- Backpressure-friendly design with async IO primitives.
- Decoupled producers/consumers and simpler agent lifecycle management.
- Clean integration with hosted services and graceful cancellation.

### Negative / Trade-offs

- Messages are not durable by default (restart loses in-flight messages).
- Delivery guarantees are best-effort unless a durability layer is added.

## Notes / Links

- Related code: `InfernalHierarchy.Core.Interfaces.IMessageBus`, `InfernalHierarchy.Messaging`
- Related docs: ../Solution-Architecture.md

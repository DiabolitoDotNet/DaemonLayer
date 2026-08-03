# Autonomy Critical Capability Matrix

## Scope

This matrix defines the minimum capability set required to defend autonomy claims in production.

Catalog version: 2026.08

## Critical capabilities

| Capability | Tool dependency | Configuration dependencies | Readiness rule |
|---|---|---|---|
| request_collaboration | request_collaboration | none | tool must be registered |
| workflow_step | workflow_step | none | tool must be registered |
| email_inbox_query | email_inbox_query | EmailInbox:Enabled, EmailInbox:Host, EmailInbox:Username, EmailInbox:Password | tool registered and inbox config complete |
| email_send | email_send | Email:Enabled, Email:Host, Email:Username, Email:Password, Email:FromAddress | tool registered and smtp config complete |
| send_telegram | send_telegram | Telegram:BotToken | tool registered and bot token configured |

## Operator evidence

The readiness API exposes this matrix status at runtime:

- GET /api/autonomy/readiness
- Includes catalogVersion
- Includes per-capability configurationDependencies and reason codes

## Certification guidance

Use the strict profile to evaluate autonomy claims:

- appsettings.AutonomyCertification.json
- FailStartupOnCriticalNotReady = true
- Strict SLO gates enabled

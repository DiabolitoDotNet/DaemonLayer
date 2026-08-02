# Incident Runbook: Startup Failures

## Signals

- Process exits on boot.
- `/health` not reachable.
- Logs show options validation failures.

## Diagnosis

1. Validate configuration and secrets (OperatorApi, Telegram, Ollama, Email).
2. Run release build locally: `dotnet build InfernalHierarchy.sln -c Release`.
3. Start host and inspect first 200 log lines for validator errors.
4. Check `/health` and `/health/ready` payload details.

## Mitigation

1. Revert recent config changes.
2. Temporarily disable optional integrations (search/voice/telegram) to isolate.
3. Restore known-good appsettings baseline.

## Rollback

1. Redeploy previous tag/image.
2. Keep current logs and failing configuration snapshot for postmortem.

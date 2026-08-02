# Incident Runbook: WebSocket Issues

## Signals

- Frequent websocket disconnects.
- Missing broadcast updates in UI.
- Elevated queue reject/drop metrics under UI load.

## Diagnosis

1. Validate auth header behavior in non-local mode.
2. Check broadcast subscriber count and queue depths.
3. Review websocket endpoint logs for handshake and close codes.
4. Confirm UI client handles fragmented frames and reconnect logic.

## Mitigation

1. Increase queue capacity or switch overflow policy if needed.
2. Reduce noisy broadcast message volume.
3. Restart affected UI sessions and re-establish subscriptions.

## Rollback

1. Roll back recent websocket/message-bus changes.
2. Restore previous operational policy and monitor depths.

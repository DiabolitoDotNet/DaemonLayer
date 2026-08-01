# Tool Authorization Debugging

This runbook explains how tool authorization behaves now and how to debug a denial quickly.

## Current behavior

- Unknown tools are denied by default.
- `custom_*` tools are Supreme-only by default.
- Explicit `ToolPermissions` entries are required to delegate either creation or invocation.
- Whitelists and blacklists are matched by agent id or agent name.

## Authorization order

1. Tool name normalization
2. Effective permission lookup
3. Global enabled check
4. Rank check
5. Blacklist check
6. Whitelist check, when configured

## Common denial reasons

- `Tool '<name>' is not configured in ToolPermissions`
- `Tool '<name>' is currently disabled`
- `Rank '<rank>' is not authorized to use tool '<name>'`
- `Agent '<name>' is not in the whitelist for tool '<name>'`
- `Custom tools are Supreme-only by default. Configure ToolPermissions to delegate.`

## Example: delegate custom-tool creation to a Duke

```json
{
  "ToolPermissions": {
    "create_custom_tool": {
      "Enabled": true,
      "AllowedRanks": "Supreme,Prince,Duke",
      "WhitelistedAgents": ["vassago"],
      "BlacklistedAgents": []
    },
    "custom_http_tool": {
      "Enabled": true,
      "AllowedRanks": "Duke",
      "WhitelistedAgents": ["vassago"],
      "BlacklistedAgents": []
    }
  }
}
```

This does two distinct things:

- grants `vassago` permission to run `create_custom_tool`,
- grants `vassago` permission to invoke `custom_http_tool` after creation.

Delegating the first one does not automatically delegate the second one.

## Structured log fields to watch

Look for authorization log entries that include:

- `agent_id`
- `agent_name`
- `tool_name`
- `rank`
- deny reason text

Also inspect startup logs for the feature snapshot and current enabled subsystems.

## Triage checklist

1. Confirm the persona or caller is trying to invoke the expected tool name.
2. Confirm the tool exists in the effective permission map.
3. Confirm the tool is enabled.
4. Confirm the caller rank is allowed.
5. Confirm blacklist/whitelist entries.
6. For `custom_*` tools, separate creation permission from invocation permission.
7. If the tool is dynamic, confirm policy approval, compilation success, and registry registration.
8. Reload permissions after config changes if the host is already running.

## Related docs

- [SECURITY_CONFIG](../../SECURITY_CONFIG.md)
- [Custom Tools Runbook](Custom-Tools.md)
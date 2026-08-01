# Custom Tools Runbook

This runbook covers the end-to-end custom tool lifecycle:

1. generate source,
2. evaluate security policy,
3. compile with Roslyn,
4. persist to LiteDB,
5. register in `ToolRegistry`,
6. invoke and debug the resulting `custom_*` tool.

## What is already implemented

- `create_custom_tool` generates and persists runtime tools.
- `custom_tool_get_source` lets you inspect persisted source/metadata.
- `ToolRegistry` replaces an existing `custom_*` implementation when a regenerated tool is registered again.
- custom tools are Supreme-only by default unless `ToolPermissions` says otherwise.

## End-to-end flow

```mermaid
flowchart LR
    operator[Operator request] --> chat[/api/chat forced invocation]
    chat --> create[create_custom_tool]
    create --> policy[DefaultCustomToolSecurityPolicy]
    policy --> store[(LiteDB custom tool store)]
    policy --> compile[Roslyn compiler]
    compile --> registry[ToolRegistry]
    registry --> invoke[custom_* invocation]
    store --> debug[custom_tool_get_source]
```

## Forced invocation format

Use the deterministic fast path when you need predictable tool execution.

Expected format:

```text
Invoke tool <name> {json}
```

For `/api/chat`, the request payload keys are PascalCase:

- `Message`
- `ToAgentId`
- `TimeoutMs`

## Recipe 1: Create a simple HTTP JSON tool

PowerShell example:

```powershell
$toolParams = @{
  requirement = 'Create an HTTP GET JSON tool using base_url and endpoint parameters'
  tool_name = 'custom_http_get_json'
  agent_id = 'lucifer'
  agent_name = 'Lucifer'
  overwrite = $true
} | ConvertTo-Json -Compress -Depth 10

$message = "Invoke tool create_custom_tool $toolParams"
$body = @{
  Message = $message
  ToAgentId = 'lucifer'
  TimeoutMs = 120000
} | ConvertTo-Json -Compress -Depth 10

Invoke-RestMethod -Method Post -Uri 'http://localhost:5080/api/chat' -ContentType 'application/json' -Body $body
```

If creation succeeds, invoke it:

```powershell
$invokeParams = @{ base_url = 'https://httpbin.org'; endpoint = '/get' } | ConvertTo-Json -Compress -Depth 10
$message = "Invoke tool custom_http_get_json $invokeParams"
$body = @{ Message = $message; ToAgentId = 'lucifer'; TimeoutMs = 120000 } | ConvertTo-Json -Compress -Depth 10

Invoke-RestMethod -Method Post -Uri 'http://localhost:5080/api/chat' -ContentType 'application/json' -Body $body
```

## Recipe 2: Diagnose a tool that does not update

1. Regenerate with `overwrite=true`.
2. Check logs for `Updated tool:` from `ToolRegistry`.
3. Inspect persisted source:

```text
Invoke tool custom_tool_get_source {"tool_name":"custom_http_get_json"}
```

4. Compare:
- `source_hash`
- `last_compiled_at`
- `last_compile_error`
- persisted source vs expected source

If the store updated but the behavior did not, verify that the newly compiled tool was re-registered and that you are invoking the same `tool_name`.

## Policy behavior

- Empty or denied source is rejected.
- Risky APIs can require manual approval.
- Comment text and string literals should not trigger risky API matches by themselves.
- Filesystem/process/reflection/native interop remain guarded.

## Common pitfalls

- `/api/chat` JSON casing must be PascalCase.
- Custom tool names should start with `custom_`.
- A Duke/Prince can be allowed to create a tool via `ToolPermissions:create_custom_tool`, but the generated `custom_*` tool is still Supreme-only until explicitly delegated.
- Network-only custom tools may compile without manual approval depending on `CustomToolsOptions`.

## Debug checklist

1. Verify `CustomTools:Enabled=true`.
2. Verify the creator agent is allowed to run `create_custom_tool`.
3. Check whether policy required manual approval.
4. Check `last_compile_error` in the persisted record.
5. Check whether the registry logged `Registered tool:` or `Updated tool:`.
6. Verify invocation permissions for the generated `custom_*` tool separately from creation permission.
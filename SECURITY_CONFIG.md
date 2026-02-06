# Security & Configuration Management Guide

## Overview

InfernalHierarchy implements enterprise-grade security and configuration management features:
- **Tool Authorization**: Rank-based and agent-specific permissions for tool execution
- **Secret Rotation**: Hot-reload secrets without application restart
- **Dynamic Configuration**: Real-time configuration updates for non-sensitive settings

## Tool Authorization

### Overview
The `ToolAuthorizationService` enforces permissions before any tool is executed by an agent.

### Configuration

Add the `ToolPermissions` section to `appsettings.json`.

For a complete, safe-by-default template (including `fs_*` and `http_request`, disabled by default), see:
- `src/InfernalHierarchy.Host/appsettings.ToolPermissions.json.example`

```json
{
  "ToolPermissions": {
    "create_sub_agent": {
      "Enabled": true,
      "AllowedRanks": "Supreme,Prince,Duke",
      "WhitelistedAgents": [],
      "BlacklistedAgents": []
    },
    "web_search": {
      "Enabled": true,
      "AllowedRanks": "Supreme,Prince,Duke,Worker",
      "WhitelistedAgents": [],
      "BlacklistedAgents": []
    },
    "write_memory": {
      "Enabled": true,
      "AllowedRanks": "Supreme,Prince,Duke",
      "WhitelistedAgents": ["Lucifer", "Baal"],
      "BlacklistedAgents": []
    }
  }
}
```

### Defaults & Overlay Behavior

- **Built-in defaults**: InfernalHierarchy starts with a default permission map for known tools.
  - This keeps new installs safe-by-default (powerful tools like filesystem/HTTP are disabled unless explicitly enabled).
- **Partial configuration overlays defaults**: If you configure only a subset of tools under `ToolPermissions`, those entries override the defaults, and the rest of the default map remains in effect.
- **Unknown tools**: If a tool name is not present in the effective permission map, it is **allowed by default** (fail-open for extensibility). See “Security Considerations” below.

### High-Risk Tools (Filesystem / HTTP / Code Execution)

Some tools are intentionally **disabled by default** and require explicit operator enablement:

- **ToolPermissions**: Enable the tool entry (e.g., `fs_read`, `http_request`, `python_exec`, `node_exec`).
- **Tool-specific options**: Enable the underlying capability (e.g., `FileSystem:Enabled`, `HttpTool:Enabled`, `CodeExecution:Enabled`).

Code execution tools run local OS processes and are a **constrained execution feature**, not a hardened security boundary.
For stronger isolation, run the Host in a container/VM and restrict the sandbox directory.

### Permission Model

**Enabled**: Global on/off switch for the tool
- `true`: Tool is available (subject to other checks)
- `false`: Tool is completely disabled for all agents

**AllowedRanks**: Comma-separated list of ranks with access
- `Supreme`: Highest authority (main agent)
- `Prince`: High-level sub-agents
- `Duke`: Mid-level agents
- `Worker`: Task-specific agents

**WhitelistedAgents**: Explicit allow list (optional)
- If empty: All agents with proper rank can use the tool
- If populated: ONLY these agents (by ID or name) can use the tool

**BlacklistedAgents**: Explicit deny list
- Specific agents (by ID or name) that are denied access
- Overrides rank permissions

### Authorization Flow

```
1. Check if tool exists in permissions config
   ├─ Not found → Allow by default (fail-open for extensibility)
   └─ Found → Continue

2. Check if tool is globally enabled
   ├─ Disabled → DENY
   └─ Enabled → Continue

3. Check agent rank against AllowedRanks
   ├─ Rank not allowed → DENY
   └─ Rank allowed → Continue

4. Check agent against BlacklistedAgents
   ├─ Agent blacklisted → DENY
   └─ Not blacklisted → Continue

5. Check WhitelistedAgents (if configured)
   ├─ List empty → ALLOW (rank check passed)
   ├─ Agent in whitelist → ALLOW
   └─ Agent not in whitelist → DENY

6. ALLOW: Tool execution proceeds
```

### Usage Example

```csharp
// Inject ToolAuthorizationService
private readonly IToolAuthorizationService _authService;

public void AuthorizeOrThrow(string agentId, string agentName, AgentRank agentRank, string toolName)
{
    var result = _authService.IsAuthorized(agentId, agentName, agentRank, toolName);

    if (!result.IsAuthorized)
    {
        _logger.LogWarning(
            "Agent {AgentId} ({Rank}) denied access to tool {Tool}: {Reason}",
            agentId, agentRank, toolName, result.Reason);

        throw new UnauthorizedAccessException(result.Reason);
    }
}
```

### Auditing

All authorization attempts are logged:

```
[INF] Tool authorization check: Agent=vassago, Rank=Duke, Tool=web_search, Authorized=True
[WRN] Tool authorization denied: Agent=generic_worker_5, Rank=Worker, Tool=create_sub_agent, Reason=RankNotAllowed
```

### Security Considerations

#### System Monitoring Commands (New)
The `/usage` and `/models` commands in ReActAgent provide **read-only system monitoring** capabilities:

- **Risk Level**: Low (no write operations, no external API calls)
- **Authorization**: Commands only execute when sent via Telegram by **AllowedUserIds**
- **Data Exposure**: 
  - Token usage stats (aggregate numbers only, no prompt content)
  - Model configurations (names, parameters - already visible in appsettings.json)
- **Recommendations**:
  1. Restrict `Telegram.AllowedUserIds` to system administrators only
  2. Consider rate limiting if agents generate high message volume
  3. Audit Telegram chat logs for command usage patterns

**Example Telegram Security Configuration:**
```json
{
  "Telegram": {
    "BotToken": "${TELEGRAM_BOT_TOKEN}",  // User secret
    "AllowedUserIds": [123456789],         // Single admin only
    "CommandRateLimit": {
      "MaxCommandsPerMinute": 10,
      "BurstSize": 3
    }
  }
}
```

#### Agent Learning Metrics
The AgentLearningService stores performance metrics in shared memory:

- **Data Stored**: Tool names, success rates, latency measurements, agent IDs
- **Privacy**: No user input or LLM response content is stored
- **Retention**: Subject to MemoryPruningService cleanup policies (default 30 days)
- **Recommendations**:
  1. Set appropriate `MemoryOptions.FactRetentionDays` for metric archival
  2. Ensure LiteDB file permissions restrict read access to application user
  3. Implement backup encryption if metrics contain sensitive operation patterns

**Example Learning Metrics Configuration:**
```json
{
  "MemoryOptions": {
    "FactRetentionDays": 30,
    "LowConfidenceThreshold": 0.3,
    "PruningIntervalHours": 24
  }
}
```

### Usage in Code

The authorization service is automatically integrated into the tool execution pipeline.
If you have a custom integration point and want to manually check:

```csharp
public class MyService
{
  private readonly IToolAuthorizationService _authService;

  public void AuthorizeOrThrow(string agentId, string agentName, AgentRank rank, string toolName)
  {
    var authResult = _authService.IsAuthorized(agentId, agentName, rank, toolName);

    if (!authResult.IsAuthorized)
    {
      _logger.LogWarning("Authorization denied: {Reason}", authResult.Reason);
      throw new UnauthorizedAccessException(authResult.Reason);
    }
  }
}
```

### Getting Authorized Tools for an Agent

```csharp
var authorizedTools = _authService.GetAuthorizedTools(agentId, agentName, rank);
// Returns: ["web_search", "read_memory", "telegram_send"]
```

### Reload Permissions Without Restart

```csharp
_authService.ReloadPermissions();
```

## Secret Rotation

### Overview
The `SecretRotationService` monitors configuration changes and hot-reloads secrets without restart.

### Supported Secrets

1. **Telegram Bot Token** (`Telegram:BotToken`)
   - Automatically recreates TelegramBotClient with new token
   - Uses `IOptionsMonitor` to detect changes

2. **Ollama Base URL** (`Ollama:BaseUrl`)
   - Updates on next request
   - OllamaClient uses `IOptionsMonitor`

3. **Brave Search API Key** (`BraveSearch:ApiKey`)
   - Updates on next request
   - BraveSearchTool uses `IOptionsMonitor`

### How It Works

1. Service monitors `IOptionsMonitor<T>` for each configuration section
2. When a change is detected:
   - Logs the change
   - Updates the relevant service
   - No restart required

3. Checks every 5 minutes for configuration file changes

### Rotating Secrets

#### Option 1: User Secrets (Recommended for Development)

```bash
# Update Telegram bot token
dotnet user-secrets set "Telegram:BotToken" "NEW_BOT_TOKEN_HERE"

# Update Brave API key
dotnet user-secrets set "BraveSearch:ApiKey" "NEW_API_KEY_HERE"
```

Changes are detected automatically within 5 minutes.

#### Option 2: Environment Variables (Production)

```bash
# Linux/Mac
export Telegram__BotToken="NEW_TOKEN"
export BraveSearch__ApiKey="NEW_KEY"

# Windows PowerShell
$env:Telegram__BotToken="NEW_TOKEN"
$env:BraveSearch__ApiKey="NEW_KEY"
```

#### Option 3: Configuration File

Edit `appsettings.json` or environment-specific files:

```json
{
  "Telegram": {
    "BotToken": "NEW_TOKEN_HERE"
  },
  "BraveSearch": {
    "ApiKey": "NEW_KEY_HERE"
  }
}
```

Save the file. Changes are detected automatically.

### Monitoring Secret Changes

Watch application logs for rotation events:

```
[12:34:56 WRN] SecretRotationService: 🔄 Telegram bot token changed - recreating client
[12:34:56 INF] TelegramBotClientFactory: 🔄 Telegram bot client recreated with new token
[12:34:56 INF] SecretRotationService: ✅ Telegram bot client updated successfully
```

## Dynamic Configuration Reload

### Overview
The `ConfigurationReloadService` monitors and reloads non-sensitive configuration changes in real-time.

### Monitored Settings

1. **HierarchyOptions** (`Hierarchy` section)
   - MainAgentPersona
   - MaxChildrenPerAgent
   - MaxAgentDepth

2. **MemoryOptions** (`Memory` section)
   - DatabasePath
   - MaxEntriesPerType

3. **SearXNGOptions** (`SearXNG` section)
   - BaseUrl
   - MaxResults
   - Enabled

### How It Works

1. Uses `IOptionsMonitor<T>` for reactive updates
2. Registers change callbacks on startup
3. When configuration file changes:
   - Detects change immediately
   - Logs configuration summary
   - New values apply on next use

### Reloading Configuration

Simply edit `appsettings.json` or environment-specific files and save:

```json
{
  "Hierarchy": {
    "MaxAgentDepth": 5,  // Changed from 4
    "MaxChildrenPerAgent": 10  // New value
  }
}
```

Changes are detected within 1 second and logged:

```
[12:34:56 INF] ConfigurationReloadService: 🔄 Configuration file reloaded (reload #1)
[12:34:56 INF] ConfigurationReloadService: 🔄 Hierarchy configuration changed:
[12:34:56 INF]   - MainAgentPersona: Lucifer
[12:34:56 INF]   - MaxChildrenPerAgent: 10
```

### Force Reload

Programmatically force a reload:

```csharp
_configuration.ForceReload();
```

## Best Practices

### Security

1. **Never commit secrets** to source control
   - Use user-secrets for development
   - Use environment variables for production
   - Keep `appsettings.json` secret-free

2. **Use least privilege** for tool permissions
   - Only grant necessary ranks access to tools
   - Use whitelists for sensitive operations
   - Regularly audit blacklisted agents

3. **Rotate secrets regularly**
   - Telegram bot tokens: Every 90 days
   - API keys: Every 30-60 days
   - Monitor rotation logs

4. **Test before production**
   - Verify new secrets work before rotating
   - Have rollback plan ready
   - Monitor error logs after rotation

### Configuration

1. **Environment-specific settings**
   - Use `appsettings.Development.json` for dev overrides
   - Use `appsettings.Production.json` for prod settings
   - Keep base `appsettings.json` with sensible defaults

2. **Hot-reload safe settings**
   - ✅ Safe: MaxResults, TimeoutSeconds, EnabledFeatures
   - ⚠️ Careful: DatabasePath, BaseUrls (may need restart)
   - ❌ Unsafe: Changing critical paths while in use

3. **Monitoring**
   - Watch for reload events in logs
   - Set up alerts on failed rotations
   - Track configuration drift over time

## Configuration Hierarchy

Configuration is loaded in this order (later overrides earlier):

1. `appsettings.json` (Base configuration)
2. `appsettings.{Environment}.json` (Environment-specific)
3. User Secrets (Development only)
4. Environment Variables (Production)
5. Command Line Arguments

Example:
```bash
# Override Telegram token via environment variable
Telegram__BotToken="prod_token" dotnet run

# Override via command line
dotnet run --Telegram:BotToken="prod_token"
```

## Troubleshooting

### Secret Rotation Not Working

1. Check service is registered in Program.cs
2. Verify `IOptionsMonitor` is used (not `IOptions`)
3. Check file permissions on configuration files
4. Review logs for rotation errors

### Configuration Changes Not Detected

1. Ensure file is saved properly
2. Check `reloadOnChange: true` in configuration builder
3. Verify service is running (check logs)
4. Try forcing reload with `_configuration.ForceReload()`

### Authorization Always Denying

1. Check tool name matches exactly (case-insensitive)
2. Verify agent rank is in AllowedRanks
3. Check agent not in BlacklistedAgents
4. If using whitelist, verify agent is in list
5. Reload permissions: `_authService.ReloadPermissions()`

## Example Scenarios

### Scenario 1: Disable Tool Temporarily

```json
{
  "ToolPermissions": {
    "web_search": {
      "Enabled": false,  // ← Disable globally
      "AllowedRanks": "Supreme,Prince,Duke,Worker"
    }
  }
}
```

Save file. Tool is immediately disabled for all agents.

### Scenario 2: Restrict Tool to Specific Agent

```json
{
  "ToolPermissions": {
    "create_sub_agent": {
      "Enabled": true,
      "AllowedRanks": "Supreme,Prince,Duke",
      "WhitelistedAgents": ["Lucifer"],  // ← Only Lucifer can create agents
      "BlacklistedAgents": []
    }
  }
}
```

### Scenario 3: Rotate Telegram Bot Token

```bash
# Get new token from @BotFather
NEW_TOKEN="7123456789:ABCdefGHIjklMNOpqrSTUvwxYZ"

# Update secret
dotnet user-secrets set "Telegram:BotToken" "$NEW_TOKEN"

# Watch logs for rotation
tail -f logs/infernal-*.log | grep "Telegram"
```

Within 5 minutes:
```
[12:45:00 WRN] 🔄 Telegram bot token changed - recreating client
[12:45:00 INF] ✅ Telegram bot client updated successfully
```

### Scenario 4: Update Memory Limits Without Restart

```json
{
  "Memory": {
    "DatabasePath": "data/infernal.db",
    "MaxEntriesPerType": 100000  // Increased from 50000
  }
}
```

Save file. New limit applies to next memory write operation.

## Performance Impact

- **Secret Rotation**: Negligible (~0.1% CPU during check)
- **Configuration Reload**: <1ms per reload
- **Tool Authorization**: <0.1ms per check (in-memory lookup)

## Security Considerations

1. **Fail-Open Design**: Unknown tools are allowed by default
   - Ensures extensibility for custom tools
   - Can be changed to fail-closed if needed

2. **No Authorization Required for Core Operations**
   - Message bus communication is always allowed
   - Agent lifecycle management is internal
   - Only tools are subject to authorization

3. **Audit Trail**: All authorization decisions are logged
   - Use Serilog sinks to send to SIEM
   - Search logs for denied attempts
   - Monitor for unusual patterns

## Summary

✅ **Implemented:**
- Tool authorization with rank and agent-level permissions
- Secret rotation without restart for Telegram, Ollama, Brave
- Dynamic configuration reload for non-sensitive settings
- Comprehensive logging and monitoring

✅ **Benefits:**
- Zero-downtime secret updates
- Fine-grained tool access control
- Real-time configuration adjustments
- Enhanced security posture

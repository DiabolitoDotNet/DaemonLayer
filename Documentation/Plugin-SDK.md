# Plugin SDK

This document defines the third-party plugin workflow for marketplace tools.

## Scope

Plugin SDK contributions target runtime discovery through ToolMarketplaceHostedService.

## Runtime contract

- Plugin assembly contains at least one public class implementing ITool.
- Constructor dependencies are created via ActivatorUtilities and host DI.
- Only allowlisted DLL files are loaded.
- Plugin size is bounded by ToolMarketplace:MaxPluginBytes.

## Packaging checklist

1. Build plugin DLL in Release mode.
2. Copy DLL to ToolMarketplace:PluginsDirectory.
3. Add file name to ToolMarketplace:AllowedPluginFiles.
4. Restart host or wait for rescan interval.
5. Verify plugin load in logs and via /api/tools.

## Starter template

Use templates/plugin-sdk as a minimal scaffold:

- InfernalPlugin.Sample.csproj
- SampleEchoTool.cs
- README.md

## Security guidance

- Keep tool outputs bounded and deterministic where possible.
- Validate all external inputs.
- Use cancellation tokens for all long-running work.
- Avoid unrestricted file/network/process access unless explicitly required and policy-reviewed.

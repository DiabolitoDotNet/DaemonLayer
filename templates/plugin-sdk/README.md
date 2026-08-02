# Plugin SDK Starter

This starter helps third-party contributors build marketplace tool plugins.

## What you get

- Minimal class library project targeting net10.0
- A sample tool implementing ITool
- Notes for packaging and allowlisting

## Quick start

1. Copy this folder to a new repository or plugin workspace.
2. Update the project name in InfernalPlugin.Sample.csproj.
3. Replace SampleEchoTool with your own tool implementation(s).
4. Build in Release and copy the output DLL to your marketplace plugins directory.
5. Add the DLL file name to ToolMarketplace:AllowedPluginFiles.

## Contract requirements

- Public non-abstract classes implementing ITool
- Constructor dependencies must be resolvable by host DI
- Tool names must be unique in a running host
- Tool implementations must respect cancellation tokens and return bounded outputs

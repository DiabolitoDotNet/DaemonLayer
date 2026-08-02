# Test Taxonomy And Execution Profiles

## Taxonomy

- Unit: isolated logic with deterministic dependencies.
- Integration: cross-component behavior with real in-process wiring.
- E2E: host startup and public endpoint behavior.
- Slow/Live: tests depending on external runtime services and network.

## Recommended Profiles

- Fast lane (PR/default local):
  - InfernalHierarchy.Core.Tests
  - InfernalHierarchy.Messaging.Tests
  - InfernalHierarchy.Tools.Tests
- Full lane (pre-merge/release):
  - dotnet test InfernalHierarchy.sln -c Release

## Commands

```powershell
# Fast lane
 dotnet test tests/InfernalHierarchy.Core.Tests/InfernalHierarchy.Core.Tests.csproj -c Release
 dotnet test tests/InfernalHierarchy.Messaging.Tests/InfernalHierarchy.Messaging.Tests.csproj -c Release
 dotnet test tests/InfernalHierarchy.Tools.Tests/InfernalHierarchy.Tools.Tests.csproj -c Release

# Full lane
 dotnet test InfernalHierarchy.sln -c Release
```

## CI Mapping

- `.github/workflows/ci.yml` implements the fast lane and full lane split.
- `.github/workflows/release.yml` includes startup health and chat smoke checks for release candidates.

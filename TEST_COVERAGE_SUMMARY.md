# Test Coverage Summary

Coverage was collected and aggregated locally on **2026-02-05** using the built-in collector (`XPlat Code Coverage`) and ReportGenerator.

## Overall

- **Line coverage**: **83.9%** (10,966 / 13,056)
- **Branch coverage**: **72.1%** (2,784 / 3,856)
- **Method coverage**: **93.0%** (1,795 / 1,929)

## Per Assembly (Line Coverage)

- **InfernalHierarchy.Core**: 90.3%
- **InfernalHierarchy.Host**: 92.3%
- **InfernalHierarchy.Messaging**: 94.7%
- **InfernalHierarchy.Personas**: 98.0%
- **InfernalHierarchy.Telegram**: 87.9%
- **InfernalHierarchy.Memory**: 81.2%
- **InfernalHierarchy.Agents**: 80.2%
- **InfernalHierarchy.Tools**: 79.3%

## Where the report is

- Aggregated report output: `coverage-report/`
  - `coverage-report/index.html`
  - `coverage-report/Summary.txt`

Note: `coverage-report/` and `TestResults/` are ignored by git (see `.gitignore`).

## Reproduce

From repo root:

```powershell
dotnet test -c Release --no-restore --collect:"XPlat Code Coverage"
dotnet tool restore
dotnet tool run reportgenerator -reports:"tests/**/coverage.cobertura.xml" -targetdir:"coverage-report" -reporttypes:"Html;TextSummary"
```

## Caveat (source file paths)

Because the repo was recently refactored (files moved into subfolders and namespaces updated), ReportGenerator may warn that some historical source paths referenced by the coverage inputs no longer exist. The aggregated percentages are still valid, but some file-level source linking in the HTML report may be incomplete until the coverage inputs fully align with the new folder layout.

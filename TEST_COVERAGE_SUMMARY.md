# Test Coverage Summary

## Update (Aug 2, 2026)

- Latest regression validation baseline is **904/904 passing tests**.
- Coverage percentages below are from the last full coverage collection run and remain the reference until the next explicit coverage capture.

Coverage was collected and aggregated locally on **2026-02-05** using the built-in collector (`XPlat Code Coverage`) and ReportGenerator.

## Overall

- **Line coverage**: **85.3%** (11,968 / 14,015)
- **Branch coverage**: **73.9%** (3,190 / 4,316)
- **Method coverage**: **94.0%** (1,994 / 2,120)

## Per Assembly (Line Coverage)

- **InfernalHierarchy.Core**: 91.0%
- **InfernalHierarchy.Host**: 91.9%
- **InfernalHierarchy.Messaging**: 95.9%
- **InfernalHierarchy.Personas**: 98.0%
- **InfernalHierarchy.Telegram**: 87.9%
- **InfernalHierarchy.Memory**: 81.2%
- **InfernalHierarchy.Agents**: 81.9%
- **InfernalHierarchy.Tools**: 83.4%

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

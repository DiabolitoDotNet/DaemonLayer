param(
    [switch]$SkipFullTests
)

$ErrorActionPreference = "Stop"

Write-Host "[quality] Restore"
dotnet restore InfernalHierarchy.sln

Write-Host "[quality] Build (Release)"
dotnet build InfernalHierarchy.sln -c Release --no-restore

Write-Host "[quality] Analyzer Build"
dotnet build InfernalHierarchy.sln -c Release --no-restore -p:RunAnalyzersDuringBuild=true

Write-Host "[quality] Fast Tests (Core, Messaging, Tools)"
dotnet test tests/InfernalHierarchy.Core.Tests/InfernalHierarchy.Core.Tests.csproj -c Release --no-build
dotnet test tests/InfernalHierarchy.Messaging.Tests/InfernalHierarchy.Messaging.Tests.csproj -c Release --no-build
dotnet test tests/InfernalHierarchy.Tools.Tests/InfernalHierarchy.Tools.Tests.csproj -c Release --no-build

if (-not $SkipFullTests) {
    Write-Host "[quality] Full Test Suite"
    dotnet test InfernalHierarchy.sln -c Release --no-build
}

Write-Host "[quality] Completed successfully"

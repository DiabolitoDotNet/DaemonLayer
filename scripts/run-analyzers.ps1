param(
    [string]$Configuration = "Release",
    [switch]$WarningsAsErrors
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "InfernalHierarchy.sln"

if (-not (Test-Path $solution)) {
    throw "Solution file not found at $solution"
}

$properties = @(
    "/p:RunAnalyzersDuringBuild=true",
    "/p:EnforceCodeStyleInBuild=true"
)

if ($WarningsAsErrors) {
    $properties += "/p:TreatWarningsAsErrors=true"
}

Push-Location $repoRoot
try {
    Write-Host "Running analyzer gate on $solution ($Configuration)..."
    & dotnet build $solution -c $Configuration @properties
    if ($LASTEXITCODE -ne 0) {
        throw "Analyzer gate failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
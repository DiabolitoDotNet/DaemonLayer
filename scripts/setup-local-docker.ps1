param(
    [string]$ComposeFile = "docker-compose.local.yml",
    [string]$OllamaHostUrl = "http://localhost:11434",
    [string]$Model = "qwen3:8b",
    [switch]$WithAutomation,
    [switch]$WithOnnx,
    [switch]$WithVoice,
    [switch]$SkipModelPull,
    [switch]$SkipOpenUi,
    [bool]$CreatePlaceholderSecrets = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Command {
    param([string]$CommandName)
    if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$CommandName' was not found in PATH."
    }
}

function Wait-Url {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 120,
        [int[]]$AcceptedStatusCodes = @(200)
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec 5 -UseBasicParsing
            if ($AcceptedStatusCodes -contains [int]$response.StatusCode) {
                return
            }
        }
        catch {
            # Retry until timeout
        }

        Start-Sleep -Seconds 2
    }

    throw "Timed out waiting for $Url"
}

function New-PlaceholderSecret {
    param(
        [string]$Path,
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }

    Set-Content -Path $Path -Value $Content -NoNewline
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
Push-Location $repoRoot

try {
    Write-Step "Checking required tools"
    Assert-Command "docker"
    Assert-Command "ollama"

    Write-Step "Checking Docker daemon"
    docker info | Out-Null

    Write-Step "Checking local Ollama endpoint"
    try {
        Invoke-WebRequest -Uri "$OllamaHostUrl/api/tags" -Method Get -TimeoutSec 8 -UseBasicParsing | Out-Null
    }
    catch {
        throw "Cannot reach Ollama at $OllamaHostUrl. Start Ollama locally first (example: ollama serve)."
    }

    if (-not $SkipModelPull) {
        Write-Step "Ensuring Ollama model '$Model' is available"
        $modelList = ollama list | Out-String
        if ($modelList -notmatch [Regex]::Escape($Model)) {
            ollama pull $Model
        }
    }

    Write-Step "Checking required secret files"
    $requiredSecrets = @{
        "secrets/telegram_bot_token.txt" = "dummy"
        "secrets/telegram_user_ids.txt" = "1"
        "secrets/telegram_lucifer_preamble.txt" = "local bootstrap"
        "secrets/email_smtp.json" = '{"enabled":false,"host":"localhost","port":25,"username":"user","password":"pass","fromAddress":"bot@example.com"}'
        "secrets/github_publisher_token.txt" = "disabled"
        "secrets/github_publisher_username.txt" = "disabled"
        "secrets/brave_search_api_key.txt" = "disabled"
    }

    $missingSecrets = @()
    foreach ($secretFile in $requiredSecrets.Keys) {
        if (-not (Test-Path $secretFile)) {
            $missingSecrets += $secretFile
        }
    }

    if ($missingSecrets.Count -gt 0) {
        if ($CreatePlaceholderSecrets) {
            Write-Step "Creating placeholder secret files for local bootstrap"
            foreach ($secretFile in $missingSecrets) {
                New-PlaceholderSecret -Path $secretFile -Content $requiredSecrets[$secretFile]
            }
        }
        else {
            throw "Missing required docker secret files:`n - " + ($missingSecrets -join "`n - ")
        }
    }

    Write-Step "Preparing local directories"
    @("data", "logs") | ForEach-Object {
        if (-not (Test-Path $_)) {
            New-Item -ItemType Directory -Path $_ | Out-Null
        }
    }

    $composeArgs = @("-f", $ComposeFile)
    if ($WithAutomation) {
        $composeArgs += @("-f", "docker-compose.automation.yml")
    }
    if ($WithOnnx) {
        $composeArgs += @("-f", "docker-compose.onnx.yml")
    }
    if ($WithVoice) {
        $composeArgs += @("-f", "docker-compose.voice.yml")
    }

    # Container -> host endpoint mapping for Ollama.
    $env:OLLAMA_BASE_URL = "$($OllamaHostUrl.TrimEnd('/'))/v1"

    Write-Step "Validating compose configuration"
    docker compose @composeArgs config | Out-Null

    Write-Step "Building and starting containers"
    docker compose @composeArgs up -d --build

    Write-Step "Waiting for API readiness"
    Wait-Url -Url "http://localhost:5080/health/ready" -TimeoutSeconds 180 -AcceptedStatusCodes @(200, 503)

    Write-Step "Waiting for UI"
    Wait-Url -Url "http://localhost:5080/ui" -TimeoutSeconds 180 -AcceptedStatusCodes @(200)

    if (-not $SkipOpenUi) {
        Write-Step "Opening UI"
        Start-Process "http://localhost:5080/ui"
    }

    Write-Host "Local stack is up. UI: http://localhost:5080/ui" -ForegroundColor Green
}
finally {
    Pop-Location
}

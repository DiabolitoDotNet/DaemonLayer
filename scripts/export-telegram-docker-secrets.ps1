[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Project = "src/InfernalHierarchy.Host/InfernalHierarchy.Host.csproj",

    [Parameter(Mandatory = $false)]
    [string]$SecretsDir = "secrets",

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-UserSecretsId([string]$ProjectPath) {
    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        throw "Project file not found: $ProjectPath"
    }

    [xml]$xml = Get-Content -LiteralPath $ProjectPath
    $node = $xml.SelectSingleNode("//UserSecretsId")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "UserSecretsId not found in project: $ProjectPath"
    }

    return $node.InnerText.Trim()
}

function Get-UserSecretsJsonPath([string]$UserSecretsId) {
    $path = Join-Path $env:APPDATA "Microsoft\\UserSecrets\\$UserSecretsId\\secrets.json"
    return $path
}

function Ensure-FilePath([string]$Path, [switch]$Force) {
    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path
        if ($item.PSIsContainer) {
            if (-not $Force.IsPresent) {
                throw "Path exists but is a directory: $Path (re-run with -Force to remove it)"
            }
            Remove-Item -LiteralPath $Path -Recurse -Force
        }
    }
}

function Write-SecretFile([string]$Path, [string]$Value) {
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }

    Set-Content -LiteralPath $Path -Value $Value -NoNewline -Encoding UTF8
}

$projectPath = $Project
$secretsDirPath = $SecretsDir

$userSecretsId = Get-UserSecretsId $projectPath
$userSecretsJson = Get-UserSecretsJsonPath $userSecretsId

if (-not (Test-Path -LiteralPath $userSecretsJson)) {
    throw "User-secrets file not found: $userSecretsJson"
}

$raw = Get-Content -LiteralPath $userSecretsJson -Raw -Encoding UTF8
$secrets = $raw | ConvertFrom-Json

$botTokenProp = $secrets.PSObject.Properties["Telegram:BotToken"]
$allowedProp = $secrets.PSObject.Properties["Telegram:AllowedUserIds"]

$botToken = if ($null -ne $botTokenProp) { $botTokenProp.Value } else { $null }
$allowed = if ($null -ne $allowedProp) { $allowedProp.Value } else { $null }

if ([string]::IsNullOrWhiteSpace($botToken)) {
    throw "Telegram:BotToken was not found in user-secrets. Set it with: dotnet user-secrets set `"Telegram:BotToken`" `"...`" --project $projectPath"
}

$botTokenPath = Join-Path $secretsDirPath "telegram_bot_token.txt"
$allowedIdsPath = Join-Path $secretsDirPath "telegram_user_ids.txt"

Ensure-FilePath -Path $botTokenPath -Force:$Force
Ensure-FilePath -Path $allowedIdsPath -Force:$Force

Write-SecretFile -Path $botTokenPath -Value ($botToken.Trim())

# AllowedUserIds in user-secrets may be:
# - an array (e.g. [123,456])
# - a string (e.g. "123,456")
# - missing
$allowedText = ""
if ($null -ne $allowed) {
    if ($allowed -is [System.Array]) {
        $allowedText = ($allowed | ForEach-Object { $_.ToString() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ","
    }
    else {
        $allowedText = $allowed.ToString().Trim()
    }
}

if (-not [string]::IsNullOrWhiteSpace($allowedText)) {
    Write-SecretFile -Path $allowedIdsPath -Value $allowedText
} elseif ($Force.IsPresent) {
    # If force, ensure file exists but empty (keeps compose secret happy)
    Write-SecretFile -Path $allowedIdsPath -Value ""
}

Write-Host "Exported Telegram docker secrets to: $botTokenPath and $allowedIdsPath" -ForegroundColor Green
Write-Host "Note: values were written to files; they were not printed to the console." -ForegroundColor DarkGray

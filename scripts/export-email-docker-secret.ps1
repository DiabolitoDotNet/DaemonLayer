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
    Join-Path $env:APPDATA "Microsoft\\UserSecrets\\$UserSecretsId\\secrets.json"
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

function Get-SecretValue($obj, [string]$key) {
    $prop = $obj.PSObject.Properties[$key]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

$userSecretsId = Get-UserSecretsId $Project
$userSecretsJson = Get-UserSecretsJsonPath $userSecretsId

if (-not (Test-Path -LiteralPath $userSecretsJson)) {
    throw "User-secrets file not found: $userSecretsJson"
}

$raw = Get-Content -LiteralPath $userSecretsJson -Raw -Encoding UTF8
$secrets = $raw | ConvertFrom-Json

$emailEnabled = Get-SecretValue $secrets 'Email:Enabled'
$emailHost = Get-SecretValue $secrets 'Email:Host'
$emailPort = Get-SecretValue $secrets 'Email:Port'
$emailUseSsl = Get-SecretValue $secrets 'Email:UseSsl'
$emailUsername = Get-SecretValue $secrets 'Email:Username'
$emailPassword = Get-SecretValue $secrets 'Email:Password'
$emailFromAddress = Get-SecretValue $secrets 'Email:FromAddress'
$emailFromName = Get-SecretValue $secrets 'Email:FromName'
$emailTimeoutMs = Get-SecretValue $secrets 'Email:TimeoutMs'

if ([string]::IsNullOrWhiteSpace($emailHost) -or [string]::IsNullOrWhiteSpace($emailUsername) -or [string]::IsNullOrWhiteSpace($emailPassword) -or [string]::IsNullOrWhiteSpace($emailFromAddress)) {
    throw "Email user-secrets are missing required fields. Ensure you set Email:Host, Email:Username, Email:Password, Email:FromAddress (use scripts/set-email-user-secrets.ps1)."
}

$enabled = $true
if ($null -ne $emailEnabled) {
    $tmp = $false
    if ([bool]::TryParse($emailEnabled.ToString(), [ref]$tmp)) { $enabled = $tmp }
}

$port = 587
if ($null -ne $emailPort) {
    $tmpInt = 0
    if ([int]::TryParse($emailPort.ToString(), [ref]$tmpInt) -and $tmpInt -gt 0) { $port = $tmpInt }
}

$useSsl = $false
if ($null -ne $emailUseSsl) {
    $tmpBool = $false
    if ([bool]::TryParse($emailUseSsl.ToString(), [ref]$tmpBool)) { $useSsl = $tmpBool }
}

$timeoutMs = 15000
if ($null -ne $emailTimeoutMs) {
    $tmpInt2 = 0
    if ([int]::TryParse($emailTimeoutMs.ToString(), [ref]$tmpInt2) -and $tmpInt2 -gt 0) { $timeoutMs = $tmpInt2 }
}

$payload = [ordered]@{
    Enabled = $enabled
    Host = $emailHost
    Port = $port
    UseSsl = $useSsl
    Username = $emailUsername
    Password = $emailPassword
    FromAddress = $emailFromAddress
    TimeoutMs = $timeoutMs
}

if ($null -ne $emailFromName -and -not [string]::IsNullOrWhiteSpace($emailFromName.ToString())) {
    $payload.FromName = $emailFromName.ToString()
}

$json = ($payload | ConvertTo-Json -Depth 4)

$outPath = Join-Path $SecretsDir "email_smtp.json"
Ensure-FilePath -Path $outPath -Force:$Force
Write-SecretFile -Path $outPath -Value $json

Write-Host "Exported Email SMTP docker secret to: $outPath" -ForegroundColor Green
Write-Host "Note: credentials were written to the secret file; they were not printed to the console." -ForegroundColor DarkGray

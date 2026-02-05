[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Project = "src/InfernalHierarchy.Host/InfernalHierarchy.Host.csproj",

    [Parameter(Mandatory = $false)]
    [switch]$Enable,

    [Parameter(Mandatory = $false)]
    [string]$SmtpHost,

    [Parameter(Mandatory = $false)]
    [int]$Port,

    [Parameter(Mandatory = $false)]
    [Nullable[bool]]$UseSsl,

    [Parameter(Mandatory = $false)]
    [string]$Username,

    [Parameter(Mandatory = $false)]
    [string]$FromAddress,

    [Parameter(Mandatory = $false)]
    [string]$FromName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PlainTextFromSecureString([securestring]$Secure) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if (-not (Test-Path -LiteralPath $Project)) {
    throw "Project file not found: $Project"
}

if ($Enable.IsPresent) {
    $enabledValue = 'true'
} else {
    $enabledValue = Read-Host "Enable email tool now? (true/false)"
    if ([string]::IsNullOrWhiteSpace($enabledValue)) { $enabledValue = 'false' }
}

if ([string]::IsNullOrWhiteSpace($SmtpHost)) {
    $SmtpHost = Read-Host "SMTP host (e.g., smtp.gmail.com)"
}

if (-not $Port -or $Port -le 0) {
    $portText = Read-Host "SMTP port (e.g., 587)"
    if (-not [int]::TryParse($portText, [ref]$Port)) {
        throw "Invalid port: $portText"
    }
}

if ($UseSsl -eq $null) {
    $useSslText = Read-Host "Use SSL from connect? (true/false). For 587 usually false (STARTTLS)."
    $tmp = $false
    if (-not [bool]::TryParse($useSslText, [ref]$tmp)) {
        throw "Invalid boolean: $useSslText"
    }
    $UseSsl = $tmp
}

if ([string]::IsNullOrWhiteSpace($Username)) {
    $Username = Read-Host "SMTP username (often your email address)"
}

if ([string]::IsNullOrWhiteSpace($FromAddress)) {
    $FromAddress = Read-Host "From address (email shown as sender)"
}

if ([string]::IsNullOrWhiteSpace($FromName)) {
    $FromName = Read-Host "From name (optional, can be blank)"
}

$securePwd = Read-Host "SMTP password / app password" -AsSecureString
$Password = Get-PlainTextFromSecureString $securePwd

Write-Host "Setting user-secrets for $Project ..." -ForegroundColor Cyan

dotnet user-secrets set "Email:Enabled" $enabledValue --project $Project

dotnet user-secrets set "Email:Host" $SmtpHost --project $Project

dotnet user-secrets set "Email:Port" "$Port" --project $Project

dotnet user-secrets set "Email:UseSsl" ($UseSsl.ToString().ToLowerInvariant()) --project $Project

dotnet user-secrets set "Email:Username" $Username --project $Project

dotnet user-secrets set "Email:Password" $Password --project $Project

dotnet user-secrets set "Email:FromAddress" $FromAddress --project $Project

if (-not [string]::IsNullOrWhiteSpace($FromName)) {
    dotnet user-secrets set "Email:FromName" $FromName --project $Project
} else {
    # Ensure no stale value remains
    dotnet user-secrets remove "Email:FromName" --project $Project | Out-Null
}

Write-Host "Done." -ForegroundColor Green

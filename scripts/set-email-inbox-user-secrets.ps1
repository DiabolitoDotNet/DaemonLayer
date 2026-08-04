[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Project = "src/InfernalHierarchy.Host/InfernalHierarchy.Host.csproj",

    [Parameter(Mandatory = $false)]
    [switch]$Enable,

    [Parameter(Mandatory = $false)]
    [string]$ImapHost,

    [Parameter(Mandatory = $false)]
    [int]$Port,

    [Parameter(Mandatory = $false)]
    [Nullable[bool]]$UseSsl,

    [Parameter(Mandatory = $false)]
    [string]$Username,

    [Parameter(Mandatory = $false)]
    [string]$Folder,

    [Parameter(Mandatory = $false)]
    [int]$TimeoutMs,

    [Parameter(Mandatory = $false)]
    [int]$MaxResults
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
}
else {
    $enabledValue = Read-Host "Enable inbox query tool now? (true/false)"
    if ([string]::IsNullOrWhiteSpace($enabledValue)) { $enabledValue = 'false' }
}

if ([string]::IsNullOrWhiteSpace($ImapHost)) {
    $ImapHost = Read-Host "IMAP host (e.g., imap.gmail.com)"
}

if (-not $Port -or $Port -le 0) {
    $portText = Read-Host "IMAP port (e.g., 993)"
    if (-not [int]::TryParse($portText, [ref]$Port)) {
        throw "Invalid port: $portText"
    }
}

if ($UseSsl -eq $null) {
    $useSslText = Read-Host "Use SSL/TLS? (true/false). For IMAP 993 this should be true."
    $tmp = $false
    if (-not [bool]::TryParse($useSslText, [ref]$tmp)) {
        throw "Invalid boolean: $useSslText"
    }
    $UseSsl = $tmp
}

if ([string]::IsNullOrWhiteSpace($Username)) {
    $Username = Read-Host "IMAP username (often your email address)"
}

if ([string]::IsNullOrWhiteSpace($Folder)) {
    $Folder = Read-Host "Mailbox folder (default: INBOX)"
    if ([string]::IsNullOrWhiteSpace($Folder)) { $Folder = 'INBOX' }
}

if (-not $TimeoutMs -or $TimeoutMs -le 0) {
    $timeoutText = Read-Host "Timeout milliseconds (default: 15000)"
    if ([string]::IsNullOrWhiteSpace($timeoutText)) {
        $TimeoutMs = 15000
    }
    elseif (-not [int]::TryParse($timeoutText, [ref]$TimeoutMs)) {
        throw "Invalid timeout: $timeoutText"
    }
}

if (-not $MaxResults -or $MaxResults -le 0) {
    $maxText = Read-Host "Default max results (1..100, default: 20)"
    if ([string]::IsNullOrWhiteSpace($maxText)) {
        $MaxResults = 20
    }
    elseif (-not [int]::TryParse($maxText, [ref]$MaxResults)) {
        throw "Invalid max results: $maxText"
    }
}

if ($MaxResults -lt 1 -or $MaxResults -gt 100) {
    throw "MaxResults must be between 1 and 100"
}

$securePwd = Read-Host "IMAP password / app password" -AsSecureString
$Password = Get-PlainTextFromSecureString $securePwd

Write-Host "Setting user-secrets for $Project ..." -ForegroundColor Cyan

dotnet user-secrets set "EmailInbox:Enabled" $enabledValue --project $Project

dotnet user-secrets set "EmailInbox:Host" $ImapHost --project $Project

dotnet user-secrets set "EmailInbox:Port" "$Port" --project $Project

dotnet user-secrets set "EmailInbox:UseSsl" ($UseSsl.ToString().ToLowerInvariant()) --project $Project

dotnet user-secrets set "EmailInbox:Username" $Username --project $Project

dotnet user-secrets set "EmailInbox:Password" $Password --project $Project

dotnet user-secrets set "EmailInbox:Folder" $Folder --project $Project

dotnet user-secrets set "EmailInbox:TimeoutMs" "$TimeoutMs" --project $Project

dotnet user-secrets set "EmailInbox:MaxResults" "$MaxResults" --project $Project

Write-Host "Done." -ForegroundColor Green

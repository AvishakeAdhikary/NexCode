[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GrantJsonPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $GrantJsonPath)) {
    throw "Grant payload '$GrantJsonPath' was not found."
}

$grantJson = Get-Content -Path $GrantJsonPath -Raw -Encoding utf8
$targetDirectory = Join-Path $env:LOCALAPPDATA 'NexCode\Auth'
$targetPath = Join-Path $targetDirectory 'superuser.grant'

New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

$protectedBytes = [System.Security.Cryptography.ProtectedData]::Protect(
    [System.Text.Encoding]::UTF8.GetBytes($grantJson),
    $null,
    [System.Security.Cryptography.DataProtectionScope]::CurrentUser)

[System.IO.File]::WriteAllBytes($targetPath, $protectedBytes)
Write-Host "Installed sealed superuser grant at $targetPath"

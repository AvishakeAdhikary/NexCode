#Requires -Version 7
<#
.SYNOPSIS
    One-click dev launcher for NexCode.
    Run from the repo root in a PowerShell 7 terminal (pwsh).
    Does NOT require elevation — Developer Mode handles sideloading.

.DESCRIPTION
    1. Builds the solution (Debug/x64)
    2. Patches the generated AppxManifest.xml (fixes PhoneProductId GUID)
    3. Registers the loose MSIX layout via Add-AppxPackage -Register
    4. Starts NexCode.Service in a new window
    5. Launches the NexCode GUI

    Re-run after any code change — it rebuilds and re-registers automatically.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── paths ────────────────────────────────────────────────────────────────────
$RepoRoot    = $PSScriptRoot
$GuiBin      = "$RepoRoot\src\NexCode.Gui\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64"
$Manifest    = "$GuiBin\AppxManifest.xml"
$ServiceExe  = "$RepoRoot\src\NexCode.Service\bin\Debug\net9.0-windows10.0.17763.0\NexCode.Service.exe"
$PkgName     = "NeuralNexusStudios.NexCode"

# ── helpers ──────────────────────────────────────────────────────────────────
function Step([string]$msg) { Write-Host "`n>>  $msg" -ForegroundColor Cyan }
function OK  ([string]$msg) { Write-Host "    OK  $msg" -ForegroundColor Green }
function Warn([string]$msg) { Write-Host "    !!  $msg" -ForegroundColor Yellow }
function Fail([string]$msg) { Write-Host "    XX  $msg" -ForegroundColor Red; exit 1 }

# ── 0.5  Kill any running NexCode processes so output DLLs are not locked ────
Step "Stopping any running NexCode processes"
@("NexCode.Gui", "NexCode.Service") | ForEach-Object {
    $procs = Get-Process -Name $_ -ErrorAction SilentlyContinue
    if ($procs) {
        $procs | Stop-Process -Force
        Write-Host "    Stopped $_"
    }
}
Start-Sleep -Milliseconds 500   # let Windows release the file handles
OK "Processes cleared"

# ── 1. Build ─────────────────────────────────────────────────────────────────
Step "Building solution (Debug / x64)"
Push-Location $RepoRoot
try {
    dotnet build NexCode.slnx --configuration Debug --verbosity quiet 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { Fail "Build failed — fix errors above, then re-run." }
    OK "Build succeeded"
} finally { Pop-Location }

# ── 2. Patch generated AppxManifest (PhoneProductId must be a real GUID) ─────
Step "Patching generated AppxManifest.xml"
if (-not (Test-Path $Manifest)) { Fail "AppxManifest.xml not found at: $Manifest" }

$xml = [xml](Get-Content $Manifest -Raw)
$ns  = @{ mp = "http://schemas.microsoft.com/appx/2014/phone/manifest" }
$phoneId = Select-Xml -Xml $xml -XPath "//mp:PhoneIdentity" -Namespace $ns |
           Select-Object -First 1 -ExpandProperty Node

$nullGuid = "00000000-0000-0000-0000-000000000000"
$guidPattern = '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'

if ($phoneId -and $phoneId.PhoneProductId -notmatch $guidPattern) {
    Warn "Fixing PhoneProductId '$($phoneId.PhoneProductId)' -> '$nullGuid'"
    $phoneId.PhoneProductId = $nullGuid
    $xml.Save($Manifest)
    OK "AppxManifest.xml patched"
} else {
    OK "AppxManifest.xml already valid"
}

# ── 3. Register the packaged layout ─────────────────────────────────────────
Step "Registering MSIX layout (Developer Mode sideload)"

$existing = Get-AppxPackage -Name $PkgName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "    Removing previous registration..."
    Remove-AppxPackage -Package $existing.PackageFullName -ErrorAction SilentlyContinue
}

Add-AppxPackage -Register $Manifest -ForceApplicationShutdown
if ($LASTEXITCODE -ne 0) { Fail "Add-AppxPackage failed. See error above." }

$pkg = Get-AppxPackage -Name $PkgName
if (-not $pkg) { Fail "Registration succeeded but package is not visible to Get-AppxPackage." }
OK "Package registered: $($pkg.PackageFullName)"

# ── 4. Start NexCode.Service ─────────────────────────────────────────────────
Step "Starting NexCode.Service"
if (-not (Test-Path $ServiceExe)) { Fail "Service exe not found: $ServiceExe" }

Get-Process -Name "NexCode.Service" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Start-Process pwsh -ArgumentList @(
    '-NoProfile', '-NoExit', '-Command',
    "Write-Host 'NexCode.Service running — close this window to stop it.' -ForegroundColor Cyan; & '$ServiceExe'"
) -WindowStyle Normal

Start-Sleep -Milliseconds 1500   # give the named pipe time to open
OK "Service started in background window"

# ── 5. Launch the GUI ────────────────────────────────────────────────────────
Step "Launching NexCode"
$appId = "$($pkg.PackageFamilyName)!App"
Start-Process "explorer.exe" "shell:AppsFolder\$appId"
OK "NexCode launched  (app id: $appId)"

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host " NexCode is running." -ForegroundColor White
Write-Host " Close the Service window (or Ctrl+C in it) to stop." -ForegroundColor DarkGray
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

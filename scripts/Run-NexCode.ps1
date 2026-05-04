#Requires -Version 7
<#
.SYNOPSIS
    One-click dev launcher for NexCode.

.DESCRIPTION
    Run from a PowerShell 7 (pwsh) terminal. Does NOT require elevation —
    Developer Mode handles MSIX sideloading.

    Steps performed:
      1. Stops any running NexCode processes (so build output isn't locked)
      2. Builds the solution (Debug / x64)
      3. Patches the generated AppxManifest.xml (fixes PhoneProductId GUID)
      4. Registers the loose MSIX layout (Add-AppxPackage -Register)
      5. Starts NexCode.Service in a new window
      6. Verifies the helper named pipe is actually accepting connections
      7. Launches the NexCode GUI

    Re-run the script after any code change to rebuild and relaunch cleanly.

.NOTES
    The helper writes its database to %LOCALAPPDATA%\NexCode\Data\nexcode.db.
    Since Slice 0011 it is SQLCipher-encrypted with a DPAPI-derived key. If a
    pre-encryption db is detected, the helper auto-quarantines it as
    nexcode.db.pre-encryption.bak and creates a fresh encrypted one.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── paths ────────────────────────────────────────────────────────────────────
$RepoRoot    = Split-Path -Parent $PSScriptRoot   # parent of scripts/
$GuiBin      = Join-Path $RepoRoot 'src\NexCode.Gui\bin\x64\Debug\net9.0-windows10.0.26100.0\win-x64'
$Manifest    = Join-Path $GuiBin   'AppxManifest.xml'
$ServiceExe  = Join-Path $RepoRoot 'src\NexCode.Service\bin\Debug\net9.0-windows10.0.17763.0\NexCode.Service.exe'
$PkgName     = "NeuralNexusStudios.NexCode"
$PipeName    = "nexcode-service-dev"

# ── helpers ──────────────────────────────────────────────────────────────────
function Step([string]$msg) { Write-Host "`n>>  $msg" -ForegroundColor Cyan }
function OK  ([string]$msg) { Write-Host "    OK  $msg" -ForegroundColor Green }
function Warn([string]$msg) { Write-Host "    !!  $msg" -ForegroundColor Yellow }
function Fail([string]$msg) { Write-Host "    XX  $msg" -ForegroundColor Red; exit 1 }

# ── 1. Stop running NexCode processes ────────────────────────────────────────
Step "Stopping any running NexCode processes"
@("NexCode.Gui", "NexCode.Service") | ForEach-Object {
    $procs = Get-Process -Name $_ -ErrorAction SilentlyContinue
    if ($procs) {
        $procs | Stop-Process -Force
        Write-Host "    Stopped $_"
    }
}
Start-Sleep -Milliseconds 500
OK "Processes cleared"

# ── 2. Build ─────────────────────────────────────────────────────────────────
Step "Building solution (Debug / x64)"
Push-Location $RepoRoot
try {
    dotnet build NexCode.slnx --configuration Debug --verbosity quiet 2>&1 | Write-Host
    if ($LASTEXITCODE -ne 0) { Fail "Build failed — fix errors above, then re-run." }
    OK "Build succeeded"
} finally { Pop-Location }

# ── 3. Patch generated AppxManifest (PhoneProductId must be a real GUID) ─────
Step "Patching generated AppxManifest.xml"
if (-not (Test-Path $Manifest)) { Fail "AppxManifest.xml not found at: $Manifest" }

$xml = [xml](Get-Content $Manifest -Raw)
$ns  = @{ mp = "http://schemas.microsoft.com/appx/2014/phone/manifest" }
$phoneId = Select-Xml -Xml $xml -XPath "//mp:PhoneIdentity" -Namespace $ns |
           Select-Object -First 1 -ExpandProperty Node

$nullGuid    = "00000000-0000-0000-0000-000000000000"
$guidPattern = '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'

if ($phoneId -and $phoneId.PhoneProductId -notmatch $guidPattern) {
    Warn "Fixing PhoneProductId '$($phoneId.PhoneProductId)' -> '$nullGuid'"
    $phoneId.PhoneProductId = $nullGuid
    $xml.Save($Manifest)
    OK "AppxManifest.xml patched"
} else {
    OK "AppxManifest.xml already valid"
}

# ── 4. Register the packaged layout ──────────────────────────────────────────
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

# ── 5. Start NexCode.Service and watch its output ────────────────────────────
Step "Starting NexCode.Service"
if (-not (Test-Path $ServiceExe)) { Fail "Service exe not found: $ServiceExe" }

# log files so the user can watch them if startup fails silently
$logDir = Join-Path $env:LOCALAPPDATA 'NexCode\Logs'
New-Item -Path $logDir -ItemType Directory -Force | Out-Null
$svcOut = Join-Path $logDir 'service.out.log'
$svcErr = Join-Path $logDir 'service.err.log'
Remove-Item $svcOut, $svcErr -ErrorAction SilentlyContinue

$svcProc = Start-Process $ServiceExe `
    -PassThru `
    -RedirectStandardOutput $svcOut `
    -RedirectStandardError  $svcErr `
    -WindowStyle Hidden

OK "Service started (PID $($svcProc.Id), logs: $logDir)"

# ── 6. Verify the helper pipe is accepting connections ──────────────────────
Step "Waiting for helper pipe to come online"

# Non-destructive check: look for the pipe in \\.\pipe\ instead of opening a
# real client connection. Opening a real connection consumes a server instance
# and triggers a benign-but-noisy dispose path on the helper side.
$pipeReady = $false
$deadline  = [DateTime]::UtcNow.AddSeconds(15)
while ([DateTime]::UtcNow -lt $deadline) {
    if ($svcProc.HasExited) { break }
    if ([System.IO.Directory]::GetFiles('\\.\pipe\') -contains "\\.\pipe\$PipeName") {
        $pipeReady = $true
        break
    }
    Start-Sleep -Milliseconds 250
}

if (-not $pipeReady) {
    if ($svcProc.HasExited) {
        Write-Host ""
        Warn "NexCode.Service crashed before the pipe came online."
        Write-Host "    Exit code:" $svcProc.ExitCode
        Write-Host "    --- stderr ---" -ForegroundColor DarkGray
        if (Test-Path $svcErr) { Get-Content $svcErr | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed } }
        Write-Host "    --- stdout (tail) ---" -ForegroundColor DarkGray
        if (Test-Path $svcOut) { Get-Content $svcOut -Tail 20 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray } }
        Fail "Helper failed to start. Logs: $logDir"
    } else {
        Fail "Helper is running (PID $($svcProc.Id)) but the pipe '$PipeName' did not open within 15s."
    }
}
OK "Helper pipe is open (named pipe '$PipeName')"

# ── 7. Launch the GUI ────────────────────────────────────────────────────────
Step "Launching NexCode"
$appId = "$($pkg.PackageFamilyName)!App"
Start-Process "explorer.exe" "shell:AppsFolder\$appId"
OK "NexCode launched  (app id: $appId)"

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host " NexCode is running." -ForegroundColor White
Write-Host " Helper PID: $($svcProc.Id)   Logs: $logDir" -ForegroundColor DarkGray
Write-Host " To stop: Get-Process NexCode.Service | Stop-Process" -ForegroundColor DarkGray
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

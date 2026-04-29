# scripts\build\Publish-SelfContained.ps1
#
# Spec §38 — produces a self-contained x64 publish of NexCode.Gui plus the MSIX
# package suitable for signing. Assumes the WAP project layout (single-project
# MSIX tooling enabled in NexCode.Gui.csproj).
#
# Usage:
#   pwsh scripts\build\Publish-SelfContained.ps1
#   pwsh scripts\build\Publish-SelfContained.ps1 -Configuration Release -Output dist\msix

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "dist\msix",
    [switch]$SkipMsix
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path "$PSScriptRoot\..\.."
Set-Location $repoRoot

$gui = Join-Path $repoRoot "src\NexCode.Gui\NexCode.Gui.csproj"
if (-not (Test-Path $gui))
{
    throw "NexCode.Gui.csproj not found at $gui."
}

Write-Host "[publish] Self-contained publish ($Configuration / $Runtime)"
& dotnet publish $gui -c $Configuration -r $Runtime --self-contained -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if ($SkipMsix)
{
    Write-Host "[publish] -SkipMsix specified, leaving MSIX unbuilt."
    return
}

$absoluteOutput = Join-Path $repoRoot $Output
$null = New-Item -ItemType Directory -Force -Path $absoluteOutput

Write-Host "[publish] Building MSIX package -> $absoluteOutput"
$msbuild = (Get-Command msbuild -ErrorAction SilentlyContinue)?.Source
if (-not $msbuild)
{
    Write-Warning "msbuild not on PATH. MSIX packaging step skipped. Install Visual Studio build tools to enable MSIX output."
    return
}

& $msbuild $gui /restore /t:Publish /p:Configuration=$Configuration /p:Platform=x64 /p:AppxPackageDir=$absoluteOutput /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never
if ($LASTEXITCODE -ne 0)
{
    throw "msbuild MSIX packaging failed with exit code $LASTEXITCODE."
}

Write-Host "[publish] MSIX output ready at $absoluteOutput"

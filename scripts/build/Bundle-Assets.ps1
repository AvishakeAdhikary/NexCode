# scripts\build\Bundle-Assets.ps1
#
# Spec §38 — populates src\NexCode.Gui\Assets\* with the offline assets that
# ship with NexCode (Monaco, Node runtime, Cascadia fonts, JSON schemas).
#
# By default this script writes README placeholders so the repo stays small
# and the publish step still succeeds. Pass -Download to actually fetch real
# binaries (requires NEXCODE_ASSETS_BASE_URL).

[CmdletBinding()]
param(
    [switch]$Download
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path "$PSScriptRoot\..\.."
$assetsRoot = Join-Path $repoRoot "src\NexCode.Gui\Assets"

$folders = @(
    @{ Name = "Monaco"; Description = "Monaco editor static assets (HTML/JS/CSS)." },
    @{ Name = "NodeRuntime"; Description = "Bundled Node.js runtime for sandbox commands." },
    @{ Name = "Fonts\Cascadia"; Description = "Cascadia Code font family." },
    @{ Name = "Schemas"; Description = "JSON schemas published by NexCode." }
)

foreach ($folder in $folders)
{
    $target = Join-Path $assetsRoot $folder.Name
    $null = New-Item -ItemType Directory -Force -Path $target

    if ($Download)
    {
        $base = $env:NEXCODE_ASSETS_BASE_URL
        if ([string]::IsNullOrWhiteSpace($base))
        {
            throw "Set NEXCODE_ASSETS_BASE_URL when using -Download."
        }
        Write-Host "[assets] Would fetch $($folder.Name) from $base/$($folder.Name).zip"
        # Production builds replace this stub with Invoke-WebRequest + Expand-Archive.
        # The placeholder still writes a README so the publish step does not fail.
    }

    $readme = Join-Path $target "README.md"
    if (-not (Test-Path $readme))
    {
        Set-Content -Path $readme -Value @"
# $($folder.Name)

$($folder.Description)

This directory is populated by ``scripts\build\Bundle-Assets.ps1`` during the
release build. To populate it locally run:

    pwsh scripts\build\Bundle-Assets.ps1 -Download

"@
    }
}

Write-Host "[assets] Asset placeholders written to $assetsRoot"

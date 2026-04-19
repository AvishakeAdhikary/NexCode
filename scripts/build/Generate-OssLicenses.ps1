[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutputPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'docs\oss-licenses.md')
)

$ErrorActionPreference = 'Stop'

$projectFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter *.csproj |
    Sort-Object FullName

$packages = foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content -Path $projectFile.FullName
    foreach ($itemGroup in $projectXml.Project.ItemGroup) {
        foreach ($packageReference in $itemGroup.PackageReference) {
            if (-not $packageReference.Include) {
                continue
            }

            $version = if ($packageReference.Version) {
                [string]$packageReference.Version
            }
            elseif ($packageReference.GetAttribute('Version')) {
                $packageReference.GetAttribute('Version')
            }
            else {
                'unspecified'
            }

            [pscustomobject]@{
                PackageId = [string]$packageReference.Include
                Version   = $version
                Project   = $projectFile.BaseName
            }
        }
    }
}

$uniquePackages = $packages |
    Sort-Object PackageId, Version, Project -Unique

$lines = @(
    '# Open Source Licenses',
    '',
    'This inventory is generated from direct `PackageReference` entries in the current repository. It is the starting point for the fuller release-time license report described in the spec.',
    '',
    '| Package | Version | Referenced By |',
    '|---|---|---|'
)

foreach ($package in $uniquePackages) {
    $lines += "| $($package.PackageId) | $($package.Version) | $($package.Project) |"
}

$outputDirectory = Split-Path -Path $OutputPath -Parent
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

Set-Content -Path $OutputPath -Value $lines -Encoding utf8

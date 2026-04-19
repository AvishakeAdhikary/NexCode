[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,

    [Parameter(Mandatory = $true)]
    [string]$CertificatePassword
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackageDirectory)) {
    throw "Package directory '$PackageDirectory' was not found."
}

if (-not (Test-Path -LiteralPath $CertificatePath)) {
    throw "Certificate '$CertificatePath' was not found."
}

$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $signtool) {
    throw "signtool.exe was not found in the Windows SDK installation."
}

$packages = Get-ChildItem -Path $PackageDirectory -Recurse -File -Include *.msix,*.appx
if ($packages.Count -eq 0) {
    throw "No MSIX or APPX packages were found under '$PackageDirectory'."
}

foreach ($package in $packages) {
    & $signtool sign /fd SHA256 /a /f $CertificatePath /p $CertificatePassword $package.FullName
}

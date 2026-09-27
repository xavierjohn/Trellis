[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Source,
    [Parameter(Mandatory)][string] $Output,
    [Parameter(Mandatory)][string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$token = '__TRELLIS_PACKAGE_VERSION__'
$readme = [System.IO.File]::ReadAllText($Source)
if (-not $readme.Contains($token, [StringComparison]::Ordinal)) {
    throw "Expected $token in $Source"
}

$directory = Split-Path -Parent $Output
New-Item -ItemType Directory -Path $directory -Force | Out-Null
[System.IO.File]::WriteAllText($Output, $readme.Replace($token, $Version),
    [System.Text.UTF8Encoding]::new($true))

<#
.SYNOPSIS
    Verifies the encoding policy for tracked repository files.
.DESCRIPTION
    Every tracked text file must be valid UTF-8 and contain no NUL bytes. C# files
    containing non-ASCII characters must include a UTF-8 BOM so legacy tools cannot
    reinterpret those source bytes. ASCII files and non-C# UTF-8 files may omit it.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$binaryExtensions = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]('.jpg', '.png'),
    [StringComparer]::OrdinalIgnoreCase)
$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$trackedFiles = @(& git -C $root -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0) {
    throw "Could not enumerate tracked files under '$root'."
}

function Get-EncodingStatus {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath,

        [Parameter(Mandatory)]
        [byte[]] $Bytes,

        [switch] $Staged
    )

    $displayPath = if ($Staged) { "$RelativePath (staged)" } else { $RelativePath }
    if ($Bytes -contains 0) {
        return [pscustomobject]@{
            Failure = "${displayPath}: tracked text must not contain NUL bytes."
            NonAsciiCSharp = $false
        }
    }

    $hasBom = $Bytes.Length -ge 3 -and
        $Bytes[0] -eq 0xEF -and
        $Bytes[1] -eq 0xBB -and
        $Bytes[2] -eq 0xBF
    $offset = if ($hasBom) { 3 } else { 0 }

    try {
        $text = $strictUtf8.GetString($Bytes, $offset, $Bytes.Length - $offset)
    }
    catch [System.Text.DecoderFallbackException] {
        return [pscustomobject]@{
            Failure = "${displayPath}: tracked text must be valid UTF-8."
            NonAsciiCSharp = $false
        }
    }

    $isNonAsciiCSharp = [System.IO.Path]::GetExtension($RelativePath).Equals(
        '.cs',
        [StringComparison]::OrdinalIgnoreCase) -and
        $text -match '[^\x00-\x7F]'
    $failure = if ($isNonAsciiCSharp -and -not $hasBom) {
        "${displayPath}: C# files containing non-ASCII characters must start with a UTF-8 BOM."
    }

    [pscustomobject]@{
        Failure = $failure
        NonAsciiCSharp = $isNonAsciiCSharp
    }
}

function Test-ByteArraysEqual {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Left,

        [Parameter(Mandatory)]
        [byte[]] $Right
    )

    if ($Left.Length -ne $Right.Length) {
        return $false
    }

    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            return $false
        }
    }

    return $true
}

$failures = [System.Collections.Generic.List[string]]::new()
$textFileCount = 0
$binaryFileCount = 0
$nonAsciiCSharpCount = 0

foreach ($relativePath in $trackedFiles) {
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        continue
    }

    $extension = [System.IO.Path]::GetExtension($relativePath)
    if ($binaryExtensions.Contains($extension)) {
        $binaryFileCount++
        continue
    }

    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $failures.Add("${relativePath}: tracked path is not a readable file.")
        continue
    }

    $textFileCount++
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $status = Get-EncodingStatus $relativePath $bytes
    if ($status.NonAsciiCSharp) {
        $nonAsciiCSharpCount++
    }
    if ($status.Failure) {
        $failures.Add($status.Failure)
    }
}

$stagedFiles = @(& git -C $root -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR)
if ($LASTEXITCODE -ne 0) {
    throw "Could not enumerate staged files under '$root'."
}

if ($stagedFiles.Count -gt 0) {
    $indexRoot = Join-Path ([System.IO.Path]::GetTempPath()) "trellis-encoding-index-$PID-$([Guid]::NewGuid().ToString('N'))"
    try {
        New-Item -ItemType Directory -Path $indexRoot | Out-Null
        $indexPrefix = $indexRoot.Replace('\', '/') + '/'

        foreach ($relativePath in $stagedFiles) {
            $extension = [System.IO.Path]::GetExtension($relativePath)
            if ($binaryExtensions.Contains($extension)) {
                continue
            }

            & git -C $root checkout-index --force "--prefix=$indexPrefix" -- $relativePath
            if ($LASTEXITCODE -ne 0) {
                throw "Could not read staged bytes for '$relativePath'."
            }

            $indexPath = Join-Path $indexRoot $relativePath
            $stagedBytes = [System.IO.File]::ReadAllBytes($indexPath)
            $workingPath = Join-Path $root $relativePath
            if ((Test-Path -LiteralPath $workingPath -PathType Leaf) -and
                (Test-ByteArraysEqual ([System.IO.File]::ReadAllBytes($workingPath)) $stagedBytes)) {
                continue
            }

            $status = Get-EncodingStatus $relativePath $stagedBytes -Staged
            if ($status.Failure) {
                $failures.Add($status.Failure)
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $indexRoot) {
            Remove-Item -LiteralPath $indexRoot -Recurse -Force
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        [Console]::Error.WriteLine("ERROR: $failure")
    }

    [Console]::Error.WriteLine(
        "Encoding policy failed with $($failures.Count) violation(s) across $textFileCount tracked text files.")
    exit 1
}

Write-Host "Encoding policy passed: $textFileCount text files, $binaryFileCount approved binary files, $nonAsciiCSharpCount non-ASCII C# files."

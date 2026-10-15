<#
.SYNOPSIS
    Exercises the tracked-file encoding policy verifier.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$verifier = Join-Path $PSScriptRoot 'verify-encoding.ps1'
if (-not (Test-Path -LiteralPath $verifier -PathType Leaf)) {
    throw "Encoding policy verifier is missing: $verifier"
}

$pwsh = (Get-Process -Id $PID).Path
$utf8 = [System.Text.UTF8Encoding]::new($false)
$utf8Bom = [System.Text.UTF8Encoding]::new($true)
$rocket = [char]::ConvertFromUtf32(0x1F680)
$workDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "trellis-encoding-policy-$PID-$([Guid]::NewGuid().ToString('N'))"

function Write-FixtureText {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Content,

        [Parameter(Mandatory)]
        [System.Text.Encoding] $Encoding
    )

    $target = Join-Path $Repository $Path
    $parent = Split-Path -Parent $target
    if ($parent) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    [System.IO.File]::WriteAllText($target, $Content, $Encoding)
}

function Write-FixtureBytes {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [byte[]] $Bytes
    )

    $target = Join-Path $Repository $Path
    $parent = Split-Path -Parent $target
    if ($parent) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    [System.IO.File]::WriteAllBytes($target, $Bytes)
}

function Invoke-PolicyCase {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [scriptblock] $Arrange,

        [Parameter(Mandatory)]
        [int] $ExpectedExitCode,

        [string] $ExpectedOutput,

        [scriptblock] $AfterStage
    )

    $repository = Join-Path $workDirectory ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $repository -Force | Out-Null

    & git -C $repository init --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Could not initialize fixture repository for '$Name'."
    }

    & $Arrange $repository
    & git -C $repository add --all
    if ($LASTEXITCODE -ne 0) {
        throw "Could not stage fixture files for '$Name'."
    }

    if ($AfterStage) {
        & $AfterStage $repository
    }

    $output = @(& $pwsh -NoProfile -File $verifier -RepositoryRoot $repository 2>&1 |
        ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne $ExpectedExitCode) {
        throw "'$Name' expected exit code $ExpectedExitCode but received $exitCode.`n$($output -join [Environment]::NewLine)"
    }

    if ($ExpectedOutput -and -not (($output -join "`n").Contains($ExpectedOutput, [StringComparison]::Ordinal))) {
        throw "'$Name' expected output '$ExpectedOutput'.`n$($output -join [Environment]::NewLine)"
    }

    Write-Host "PASS $Name"
}

try {
    New-Item -ItemType Directory -Path $workDirectory | Out-Null

    Invoke-PolicyCase -Name 'ASCII C# without BOM is valid' -ExpectedExitCode 0 -Arrange {
        param($repository)
        Write-FixtureText $repository 'Program.cs' 'internal sealed class Program { }' $utf8
    }

    Invoke-PolicyCase -Name 'non-ASCII C# with BOM is valid' -ExpectedExitCode 0 -Arrange {
        param($repository)
        Write-FixtureText $repository 'Program.cs' "internal static class Symbols { public const string Rocket = `"$rocket`"; }" $utf8Bom
    }

    Invoke-PolicyCase -Name 'non-ASCII C# without BOM is rejected' -ExpectedExitCode 1 `
        -ExpectedOutput 'Program.cs: C# files containing non-ASCII characters must start with a UTF-8 BOM.' -Arrange {
        param($repository)
        Write-FixtureText $repository 'Program.cs' "internal static class Symbols { public const string Rocket = `"$rocket`"; }" $utf8
    }

    Invoke-PolicyCase -Name 'invalid staged bytes cannot hide behind a valid working tree' -ExpectedExitCode 1 `
        -ExpectedOutput 'Program.cs (staged): C# files containing non-ASCII characters must start with a UTF-8 BOM.' -Arrange {
        param($repository)
        Write-FixtureText $repository 'Program.cs' "internal static class Symbols { public const string Rocket = `"$rocket`"; }" $utf8
    } -AfterStage {
        param($repository)
        Write-FixtureText $repository 'Program.cs' "internal static class Symbols { public const string Rocket = `"$rocket`"; }" $utf8Bom
    }

    Invoke-PolicyCase -Name 'non-ASCII Markdown without BOM is valid' -ExpectedExitCode 0 -Arrange {
        param($repository)
        Write-FixtureText $repository 'README.md' "# Launch $rocket" $utf8
    }

    Invoke-PolicyCase -Name 'invalid UTF-8 is rejected' -ExpectedExitCode 1 `
        -ExpectedOutput 'notes.txt: tracked text must be valid UTF-8.' -Arrange {
        param($repository)
        Write-FixtureBytes $repository 'notes.txt' ([byte[]](0xC3, 0x28))
    }

    Invoke-PolicyCase -Name 'NUL bytes in text are rejected' -ExpectedExitCode 1 `
        -ExpectedOutput 'notes.txt: tracked text must not contain NUL bytes.' -Arrange {
        param($repository)
        Write-FixtureBytes $repository 'notes.txt' ([byte[]](0x41, 0x00, 0x42))
    }

    Invoke-PolicyCase -Name 'approved binary extensions are ignored' -ExpectedExitCode 0 -Arrange {
        param($repository)
        Write-FixtureBytes $repository 'images/icon.png' ([byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
    }

    Invoke-PolicyCase -Name 'unapproved binary extensions are rejected' -ExpectedExitCode 1 `
        -ExpectedOutput 'payload.dat: tracked text must be valid UTF-8.' -Arrange {
        param($repository)
        Write-FixtureBytes $repository 'payload.dat' ([byte[]](0xFF, 0xD8, 0xFF))
    }

    Invoke-PolicyCase -Name 'paths containing spaces are supported' -ExpectedExitCode 0 -Arrange {
        param($repository)
        Write-FixtureText $repository 'folder/file name.cs' 'internal sealed class SpacedPath { }' $utf8
    }
}
finally {
    if (Test-Path -LiteralPath $workDirectory) {
        Remove-Item -LiteralPath $workDirectory -Recurse -Force
    }
}

Write-Host 'Encoding policy tests passed.'

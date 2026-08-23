#Requires -Version 7.0
<#
.SYNOPSIS
    Replays api.http against a running Showcase host and prints every response.

.DESCRIPTION
    api.http documents this sample's HTTP contract, including the status code each
    request is expected to produce and the error shape behind it. Nothing verified
    those claims: the file is prose that happens to be executable, so it could drift
    from the running code silently.

    This script executes it. Each request is sent in file order and checked against
    its `# @expect` directives, and the full response -- status, headers, and
    pretty-printed body -- is written to a transcript.

    The transcript is the point. When the Error ADT or the ProblemDetails mapping
    changes, `git diff` over two transcripts shows exactly what consumers will see,
    across every error path the sample exercises, rather than leaving it to be
    inferred from unit tests. The same diff between the two hosts is a parity check:
    api.http claims MVC and Minimal API behave identically, and a transcript diff is
    what makes that claim falsifiable.

.PARAMETER Environment
    Which host block of http-client.env.json to use: `mvc` or `minimalapi`.

.PARAMETER StartHost
    Start the matching host, wait for it to answer, replay, then stop it. Without
    this the host is assumed to be running already.

.PARAMETER TranscriptPath
    Where to write the transcript. Defaults to replay-<environment>.txt beside the
    script. Pass an explicit path to keep a baseline for diffing. The header carries a
    'started' timestamp, so two transcripts of identical runs differ on that one line;
    skip the header when diffing.

.PARAMETER Set
    Overrides for variables in http-client.env.json, as name=value.

.NOTES
    The expectations encode the seeded state -- balances, account statuses, and an
    empty idempotency store -- so a replay assumes a freshly started host. Against a
    host that has already served a replay, the transfer blocks report a replay where
    a fresh execution is expected. Use -StartHost, or restart the host by hand.

.EXAMPLE
    ./replay-api-http.ps1 -Environment mvc -StartHost

.EXAMPLE
    # Compare the two hosts, or the same host before and after a change.
    ./replay-api-http.ps1 -Environment mvc -StartHost -TranscriptPath mvc.txt
    ./replay-api-http.ps1 -Environment minimalapi -StartHost -TranscriptPath minimal.txt
    git diff --no-index mvc.txt minimal.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('mvc', 'minimalapi')]
    [string]$Environment,

    [switch]$StartHost,

    [string]$TranscriptPath,

    [string[]]$Set = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = $PSScriptRoot
$httpFile = Join-Path $scriptRoot 'api.http'
$envFile = Join-Path $scriptRoot 'http-client.env.json'

if (-not $TranscriptPath) { $TranscriptPath = Join-Path $scriptRoot "replay-$Environment.txt" }
if (-not [System.IO.Path]::IsPathRooted($TranscriptPath)) {
    $TranscriptPath = Join-Path (Get-Location) $TranscriptPath
}

$hostProjects = @{
    'mvc'        = 'src/Showcase.Mvc'
    'minimalapi' = 'src/Showcase.MinimalApi'
}

#region variables

$environmentJson = Get-Content $envFile -Raw | ConvertFrom-Json
$variables = [ordered]@{}
foreach ($property in $environmentJson.'$shared'.PSObject.Properties) {
    $variables[$property.Name] = [string]$property.Value
}
foreach ($property in $environmentJson.$Environment.PSObject.Properties) {
    $variables[$property.Name] = [string]$property.Value
}
foreach ($override in $Set) {
    if ($override -notmatch '^([^=]+)=(.*)$') {
        throw "-Set expects name=value, got '$override'."
    }
    $variables[$Matches[1]] = $Matches[2]
}

if (-not $variables.Contains('host')) {
    throw "http-client.env.json has no 'host' for environment '$Environment'."
}

$baseUri = [uri]$variables['host']

function Expand-Variable([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return $text }
    foreach ($name in $variables.Keys) {
        $text = $text.Replace("{{$name}}", $variables[$name])
    }
    return $text
}

#endregion

#region parsing

class HttpRequestBlock {
    [string]$Title
    [string]$Method
    [string]$Url
    [hashtable]$Headers = @{}
    [string]$Body
    [nullable[int]]$ExpectedStatusMin
    [nullable[int]]$ExpectedStatusMax
    [string]$ExpectedStatusText
    [string[]]$ExpectedHeaders = @()
    [string]$ExpectedContentType

    # Mirrors HttpFileParser's rule for materializing an ExpectedOutcome: once the author
    # declares anything, the harness asserts exactly what was declared and stops applying
    # its own default. The two parsers read the same file, so they must agree on this.
    [bool] HasExpectations() {
        return $null -ne $this.ExpectedStatusMin -or
               $this.ExpectedHeaders.Count -gt 0 -or
               [bool]$this.ExpectedContentType
    }
}

function Read-HttpFile([string]$path) {
    $blocks = [System.Collections.Generic.List[HttpRequestBlock]]::new()
    $current = $null
    $state = 'none'

    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        # A ### line both separates requests and titles the one that follows. A request
        # is often preceded by several ### commentary lines, so only the first one
        # titles the block -- otherwise the last line of a long preamble wins and the
        # transcript is labelled with a sentence fragment. Banner lines of nothing but
        # # are decoration and carry no title.
        if ($line -match '^###') {
            if ($null -ne $current -and $current.Method) {
                $blocks.Add($current)
                $current = $null
            }
            # A rule of nothing but hashes is a visual divider, not a request title. Reset
            # hard so prose above it -- including a file header that *documents* a directive
            # by quoting it -- cannot leak expectations onto the first real request below.
            if ($line -match '^#+$') {
                $current = $null
            }
            if ($null -eq $current) {
                $current = [HttpRequestBlock]::new()
                $state = 'none'
            }
            $candidate = ($line -replace '^#+', '').Trim()
            if (-not $current.Title -and $candidate -and $candidate -notmatch '^#*$') {
                $current.Title = $candidate
            }
            continue
        }

        if ($null -eq $current) { continue }

        if ($state -ne 'body' -and $line -match '^\s*#') {
            # Accept every status form HttpFileParser accepts. Matching only \d+ here would
            # let `2xx` and `200-299` parse as "no expectation" and quietly fall through to
            # the default -- an assertion the author wrote but never got.
            if ($line -match '@expect\s+status:\s*(\d{3})\s*-\s*(\d{3})') {
                $current.ExpectedStatusMin = [int]$Matches[1]
                $current.ExpectedStatusMax = [int]$Matches[2]
                $current.ExpectedStatusText = "$($Matches[1])-$($Matches[2])"
            }
            elseif ($line -match '@expect\s+status:\s*(\d)xx') {
                $current.ExpectedStatusMin = [int]$Matches[1] * 100
                $current.ExpectedStatusMax = $current.ExpectedStatusMin + 99
                $current.ExpectedStatusText = "$($Matches[1])xx"
            }
            elseif ($line -match '@expect\s+status:\s*(\d+)') {
                $current.ExpectedStatusMin = [int]$Matches[1]
                $current.ExpectedStatusMax = [int]$Matches[1]
                $current.ExpectedStatusText = $Matches[1]
            }
            elseif ($line -match '@expect\s+content-type:\s*(\S+)') {
                $current.ExpectedContentType = $Matches[1]
            }
            elseif ($line -match '@expect\s+header:\s*(\S+)') {
                $current.ExpectedHeaders += $Matches[1]
            }
            continue
        }

        switch ($state) {
            'none' {
                if ([string]::IsNullOrWhiteSpace($line)) { break }
                if ($line -match '^(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\s+(\S+)') {
                    $current.Method = $Matches[1]
                    $current.Url = Expand-Variable $Matches[2]
                    $state = 'headers'
                }
                break
            }
            'headers' {
                if ([string]::IsNullOrWhiteSpace($line)) { $state = 'body'; break }
                if ($line -match '^([A-Za-z0-9\-]+):\s*(.*)$') {
                    $current.Headers[$Matches[1]] = Expand-Variable $Matches[2]
                }
                break
            }
            'body' {
                $current.Body = if ($null -eq $current.Body) { Expand-Variable $line }
                else { $current.Body + "`n" + (Expand-Variable $line) }
                break
            }
        }
    }

    if ($null -ne $current -and $current.Method) { $blocks.Add($current) }
    return $blocks
}

#endregion

#region host lifecycle

$hostProcess = $null

function Start-ShowcaseHost {
    # Refuse to start on top of a host that is already listening. Otherwise the
    # readiness probe below can be answered by that older process before the one
    # started here finishes binding -- the replay then runs against a host with
    # unknown state, and the finally block stops the wrong process and leaves the
    # real one running.
    $probe = [uri]::new($baseUri, '/api/accounts?limit=1')
    try {
        $null = Invoke-WebRequest -Uri $probe -SkipCertificateCheck -SkipHttpErrorCheck -TimeoutSec 5
        throw "Something is already answering at $baseUri. Stop it first, or omit -StartHost to replay against it."
    }
    catch [System.Net.Http.HttpRequestException] {
        # Nothing listening, which is what -StartHost requires.
    }

    $projectPath = Join-Path $scriptRoot $hostProjects[$Environment]
    Write-Host "Starting $Environment host from $projectPath ..." -ForegroundColor DarkGray

    $startArgs = @{
        FilePath     = 'dotnet'
        ArgumentList = @('run', '-c', 'Release', '--project', $projectPath)
        PassThru     = $true
    }

    # -WindowStyle throws a terminating NotSupportedException off Windows, so it
    # can only be passed conditionally. $IsWindows is very slightly wider than the
    # check Start-Process applies (!IsNanoServer && !IsIoT), so Nano Server and
    # Windows IoT would still throw -- neither ships the .NET SDK this needs.
    if ($IsWindows) {
        $startArgs.WindowStyle = 'Hidden'
    }

    $process = Start-Process @startArgs

    $deadline = (Get-Date).AddSeconds(180)

    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            throw "The $Environment host exited with code $($process.ExitCode) before it answered."
        }
        try {
            $null = Invoke-WebRequest -Uri $probe -SkipCertificateCheck -SkipHttpErrorCheck -TimeoutSec 5
            Write-Host "Host is answering at $baseUri" -ForegroundColor DarkGray
            return $process
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }

    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "The $Environment host did not answer at $baseUri within 180s."
}

#endregion

#region response formatting

function Format-Body($body, [string]$contentType) {
    # Invoke-WebRequest hands back a byte[] rather than a string when it does not
    # recognise the content type as text, which is exactly what happens to
    # `application/problem+json` when the host omits a charset. Decoding here keeps
    # an error payload readable instead of printing it as a wall of byte values.
    if ($body -is [byte[]]) {
        $body = [System.Text.Encoding]::UTF8.GetString($body)
    }

    if ([string]::IsNullOrWhiteSpace([string]$body)) { return '(empty)' }
    $body = [string]$body

    if ($contentType -match 'json') {
        try {
            return ($body | ConvertFrom-Json -Depth 40 | ConvertTo-Json -Depth 40)
        }
        catch {
            # Fall through: an unparseable body is itself worth seeing verbatim.
        }
    }

    return $body
}

# Headers that change on every run would turn every transcript diff into noise,
# which defeats the purpose of keeping a baseline.
$volatileHeaders = @('Date', 'Server', 'Transfer-Encoding', 'Connection', 'Keep-Alive')

function Format-Headers($headers) {
    $lines = foreach ($key in ($headers.Keys | Sort-Object)) {
        if ($volatileHeaders -contains $key) { continue }
        "$key`: $($headers[$key] -join ', ')"
    }
    if (-not $lines) { return '(none)' }
    return ($lines -join "`n")
}

#endregion

$requests = Read-HttpFile $httpFile
if ($requests.Count -eq 0) {
    throw "No requests parsed from $httpFile. The file format may have changed."
}

try {
    if ($StartHost) { $hostProcess = Start-ShowcaseHost }

    $transcript = [System.Text.StringBuilder]::new()
    $null = $transcript.AppendLine("Showcase api.http replay")
    $null = $transcript.AppendLine("environment : $Environment")
    $null = $transcript.AppendLine("host        : $baseUri")
    $null = $transcript.AppendLine("requests    : $($requests.Count)")
    $null = $transcript.AppendLine("started     : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
    $null = $transcript.AppendLine()

    $results = [System.Collections.Generic.List[object]]::new()
    $number = 0

    foreach ($request in $requests) {
        $number++

        $sendHeaders = @{}
        $contentType = $null
        foreach ($key in $request.Headers.Keys) {
            if ($key -ieq 'Content-Type') { $contentType = $request.Headers[$key] }
            else { $sendHeaders[$key] = $request.Headers[$key] }
        }

        $arguments = @{
            Uri                  = $request.Url
            Method               = $request.Method
            Headers              = $sendHeaders
            SkipCertificateCheck = $true
            SkipHttpErrorCheck   = $true
            MaximumRedirection   = 0
        }

        $body = if ($request.Body) { $request.Body.Trim() } else { '' }
        if ($body) {
            $arguments.Body = $body
            $arguments.ContentType = if ($contentType) { $contentType } else { 'application/json' }
        }

        $transportError = $null
        try {
            $response = Invoke-WebRequest @arguments
            $status = [int]$response.StatusCode
            $responseHeaders = $response.Headers
            $responseBody = $response.Content
            $responseType = if ($response.Headers.ContainsKey('Content-Type')) { $response.Headers['Content-Type'] -join ';' } else { '' }
        }
        catch {
            $transportError = $_.Exception.Message
            $status = $null
            $responseHeaders = @{}
            $responseBody = ''
            $responseType = ''
        }

        # With no expectations declared at all, fall back to the same non-error net the C#
        # runner uses (100-399) rather than a stricter 2xx, so the two disagree about
        # nothing. Once anything is declared, assert that and only that.
        $expectedText = if ($request.ExpectedStatusText) { $request.ExpectedStatusText }
        elseif ($request.HasExpectations()) { '-' }
        else { '1xx-3xx' }

        $matched = if ($null -eq $status) { $false }
        elseif ($null -ne $request.ExpectedStatusMin) {
            $status -ge $request.ExpectedStatusMin -and $status -le $request.ExpectedStatusMax
        }
        elseif ($request.HasExpectations()) { $true }
        else { $status -ge 100 -and $status -lt 400 }

        # Match HttpFileAssertions: a required header must be present *and non-empty*.
        # Presence alone would let an empty ETag pass here while the shipped C# runner
        # rejects it -- the same file, two verdicts.
        $missingHeaders = @()
        foreach ($expectedHeader in $request.ExpectedHeaders) {
            $key = $responseHeaders.Keys | Where-Object { $_ -ieq $expectedHeader } | Select-Object -First 1
            $value = if ($key) { ($responseHeaders[$key] -join ', ') } else { $null }
            if ([string]::IsNullOrEmpty($value)) {
                $missingHeaders += $expectedHeader
                $matched = $false
            }
        }

        # Compare media type only: responses carry `; charset=utf-8`, and a directive that
        # forced authors to spell that out would go unused. This assertion exists because
        # [Produces("application/json")] rewrites a problem document's media type while
        # leaving status and body correct -- invisible to every other check here.
        if ($request.ExpectedContentType) {
            $actualMediaType = ($responseType -split ';')[0].Trim()
            $wantedMediaType = ($request.ExpectedContentType -split ';')[0].Trim()
            if ($actualMediaType -ine $wantedMediaType) {
                $missingHeaders += "content-type=$(if ($actualMediaType) { $actualMediaType } else { '<none>' })"
                $matched = $false
            }
        }

        $results.Add([pscustomobject]@{
                N        = $number
                Method   = $request.Method
                Path     = ([uri]$request.Url).PathAndQuery
                Expected = $expectedText
                Actual   = if ($null -ne $status) { $status } else { 'ERROR' }
                Matched  = $matched
                Missing  = ($missingHeaders -join ', ')
                Title    = $request.Title
            })

        $null = $transcript.AppendLine('=' * 78)
        $null = $transcript.AppendLine("[$number] $($request.Method) $(([uri]$request.Url).PathAndQuery)")
        if ($request.Title) { $null = $transcript.AppendLine("     $($request.Title)") }
        $null = $transcript.AppendLine('=' * 78)
        if ($body) {
            $null = $transcript.AppendLine('--- request body ---')
            $null = $transcript.AppendLine((Format-Body $body 'json'))
        }
        $null = $transcript.AppendLine("--- response: expected $expectedText, got $(if ($null -ne $status) { $status } else { 'transport error' }) $(if ($matched) { '[match]' } else { '[MISMATCH]' }) ---")
        if ($transportError) {
            $null = $transcript.AppendLine($transportError)
        }
        else {
            if ($missingHeaders) {
                $null = $transcript.AppendLine("missing expected header(s): $($missingHeaders -join ', ')")
            }
            $null = $transcript.AppendLine((Format-Headers $responseHeaders))
            $null = $transcript.AppendLine()
            $null = $transcript.AppendLine((Format-Body $responseBody $responseType))
        }
        $null = $transcript.AppendLine()
    }

    $matchedCount = ($results | Where-Object Matched).Count
    $summary = "$matchedCount/$($results.Count) requests matched their expectations"
    $null = $transcript.AppendLine('=' * 78)
    $null = $transcript.AppendLine($summary)

    [System.IO.File]::WriteAllText($TranscriptPath, $transcript.ToString(), (New-Object System.Text.UTF8Encoding $true))

    $results | Format-Table N, Method, Path, Expected, Actual, Matched, Missing -AutoSize | Out-String -Width 200 | Write-Host
    Write-Host $summary -ForegroundColor Cyan
    Write-Host "Transcript: $TranscriptPath" -ForegroundColor DarkGray

    if ($matchedCount -ne $results.Count) {
        Write-Host "api.http no longer describes what the host does. Fix the host, or update the file's @expect directives." -ForegroundColor Red
        exit 1
    }
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Write-Host "Stopping $Environment host (pid $($hostProcess.Id)) ..." -ForegroundColor DarkGray
        Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
    }
}

#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Runs the LLM doc-quality Q&A bench against Azure AI Foundry.

.DESCRIPTION
  Loads questions.json, concatenates the docfx doc set as context, calls a
  hosted chat model once with the full bank, parses the JSON answer array,
  writes results to results/<UTC-timestamp>-<model>-<tag>.json, and
  optionally invokes score-qa-bench.ps1.

  The bench is designed to A/B-test docfx documentation changes (e.g.,
  splitting oversized docs). Run baseline before the change; run again
  after; compare scores.

.PARAMETER Tag
  Short label for the run (e.g., 'baseline', 'phase1-split'). Goes into the
  output filename.

.PARAMETER Model
  Model deployment name in Foundry. Defaults to env:AZURE_AI_MODEL or 'gpt-4.1'.

.PARAMETER ApiVersion
  Azure API version. Defaults to '2024-05-01-preview'.

.PARAMETER Iterations
  Number of repeat runs (for variance estimation). Default 1.

.PARAMETER Score
  After the run, invoke score-qa-bench.ps1 on the result.

.PARAMETER DryRun
  Build the prompt and write to results/dry-run-<tag>.txt; do NOT call the API.

.PARAMETER Temperature
  Sampling temperature. Default 0 for reproducibility.

.PARAMETER Seed
  Sampling seed. Default 42.

.PARAMETER QuestionsPath
  Override the questions JSON file. Default: ./questions.json next to this script.

.ENVIRONMENT
  AZURE_AI_ENDPOINT  required, e.g., https://xavaifoundry.services.ai.azure.com
  AZURE_AI_KEY       required, the api-key value
  AZURE_AI_MODEL     optional, default 'gpt-4.1'

.EXAMPLE
  $env:AZURE_AI_ENDPOINT='https://xxx.services.ai.azure.com'
  $env:AZURE_AI_KEY='...'
  pwsh ./run-qa-bench.ps1 -Tag baseline -Score
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Tag,
    [string] $Model,
    [string] $ApiVersion = '2024-05-01-preview',
    [int]    $Iterations = 1,
    [switch] $Score,
    [switch] $DryRun,
    [double] $Temperature = 0,
    [int]    $Seed = 42,
    [string] $QuestionsPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- locate paths -----------------------------------------------------------
$benchRoot   = Split-Path -Parent $PSCommandPath
$repoRoot    = Resolve-Path (Join-Path $benchRoot '../../..')
$articleDir  = Join-Path $benchRoot '../articles'
$apiRefDir   = Join-Path $benchRoot '../api_reference'
$resultsDir  = Join-Path $benchRoot 'results'
if (-not $QuestionsPath) { $QuestionsPath = Join-Path $benchRoot 'questions.json' }
if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir | Out-Null }

# --- env / model ------------------------------------------------------------
$endpoint = $env:AZURE_AI_ENDPOINT
$apiKey   = $env:AZURE_AI_KEY
if (-not $Model) { $Model = if ($env:AZURE_AI_MODEL) { $env:AZURE_AI_MODEL } else { 'gpt-4.1' } }

if (-not $DryRun) {
    if (-not $endpoint) { throw 'AZURE_AI_ENDPOINT env var is required (or pass -DryRun).' }
    if (-not $apiKey)   { throw 'AZURE_AI_KEY env var is required (or pass -DryRun).' }
}
if ($endpoint) { $endpoint = $endpoint.TrimEnd('/') }

# --- load questions ---------------------------------------------------------
$questionsRaw = Get-Content -Raw -Path $QuestionsPath
$questionsObj = $questionsRaw | ConvertFrom-Json
$questions    = @($questionsObj.questions)
Write-Host ("Loaded {0} questions from {1}" -f $questions.Count, (Split-Path -Leaf $QuestionsPath))

# --- build doc context ------------------------------------------------------
$docFiles = @()
$docFiles += Get-ChildItem -Path $articleDir -Filter '*.md' -File | Sort-Object Name
$docFiles += Get-ChildItem -Path $apiRefDir  -Filter '*.md' -File | Sort-Object Name

$contextSb = [System.Text.StringBuilder]::new(1MB)
[void] $contextSb.AppendLine('<BEGIN DOCUMENTATION>')
foreach ($f in $docFiles) {
    $rel = $f.FullName.Substring($repoRoot.Path.Length + 1).Replace('\','/')
    [void] $contextSb.AppendLine("=== FILE: $rel ===")
    [void] $contextSb.AppendLine((Get-Content -Raw -Path $f.FullName))
}
[void] $contextSb.AppendLine('<END DOCUMENTATION>')
$context = $contextSb.ToString()
$approxTokens = [math]::Round($context.Length / 4)
Write-Host ("Doc set: {0} files, {1:N0} chars (~{2:N0} tokens)" -f $docFiles.Count, $context.Length, $approxTokens)

# --- build question block ---------------------------------------------------
$qSb = [System.Text.StringBuilder]::new()
[void] $qSb.AppendLine('Answer each of the following questions, using ONLY the documentation above. Do not invent facts. Do not call tools. Use no external knowledge.')
[void] $qSb.AppendLine('Reply with a single JSON array. Each element MUST have exactly these fields: { "id": string, "answer": string }. Keep each answer brief and directly responsive to the question - no markdown, no commentary, no leading explanation. Do NOT wrap the JSON in code fences.')
[void] $qSb.AppendLine('')
[void] $qSb.AppendLine('Questions:')
foreach ($q in $questions) {
    [void] $qSb.AppendLine(("[{0}] {1}" -f $q.id, $q.question))
}
$questionBlock = $qSb.ToString()

# --- assemble messages ------------------------------------------------------
$systemMsg = @'
You are a meticulous Trellis framework expert. You answer questions about the Trellis .NET framework strictly from the provided documentation excerpt. You return responses as a single JSON array per the user's instructions, with no surrounding prose, no code fences, and no commentary.
'@
$userMsg = "$context`n`n$questionBlock"

# --- dry run path -----------------------------------------------------------
if ($DryRun) {
    $safeTag = ($Tag -replace '[^A-Za-z0-9._-]','_')
    $dryPath = Join-Path $resultsDir ("dry-run-{0}.txt" -f $safeTag)
    @(
        "=== SYSTEM ===",
        $systemMsg,
        "",
        "=== USER (length=$($userMsg.Length) chars, ~$approxTokens tokens) ===",
        $userMsg
    ) -join "`n" | Out-File -FilePath $dryPath -Encoding utf8
    Write-Host "DRY RUN written to $dryPath"
    return
}

# --- API call (one or more iterations) --------------------------------------
$uri = "$endpoint/models/chat/completions?api-version=$ApiVersion"
$headers = @{ 'api-key' = $apiKey; 'Content-Type' = 'application/json' }

for ($iter = 1; $iter -le $Iterations; $iter++) {
    $iterTag = if ($Iterations -gt 1) { "$Tag-iter$iter" } else { $Tag }
    $safeIterTag = ($iterTag -replace '[^A-Za-z0-9._-]','_')
    Write-Host ("`n=== Run {0}/{1} (tag='{2}', model='{3}') ===" -f $iter, $Iterations, $iterTag, $Model)

    $body = @{
        model       = $Model
        temperature = $Temperature
        seed        = $Seed
        max_tokens  = 4000
        messages    = @(
            @{ role = 'system'; content = $systemMsg },
            @{ role = 'user';   content = $userMsg }
        )
    } | ConvertTo-Json -Depth 6 -Compress

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $resp = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $body -ErrorAction Stop -TimeoutSec 600
    } catch {
        Write-Host "API call failed: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.ErrorDetails.Message) { Write-Host "Body: $($_.ErrorDetails.Message)" -ForegroundColor Red }
        throw
    }
    $sw.Stop()

    $finish    = $resp.choices[0].finish_reason
    $modelOut  = $resp.model
    $usage     = $resp.usage
    $contentRaw= $resp.choices[0].message.content

    Write-Host ("  finish={0}  model={1}  prompt_tokens={2}  completion_tokens={3}  duration={4:N1}s" -f `
        $finish, $modelOut, $usage.prompt_tokens, $usage.completion_tokens, $sw.Elapsed.TotalSeconds)

    # parse: strip code fences if present, then ConvertFrom-Json
    $content = $contentRaw.Trim()
    if ($content.StartsWith('```')) {
        $content = ($content -replace '^```(json)?\s*\r?\n', '') -replace '\r?\n```\s*$', ''
    }
    try {
        $parsed = $content | ConvertFrom-Json -ErrorAction Stop
    } catch {
        Write-Host "Failed to parse JSON answer; saving raw content for inspection." -ForegroundColor Yellow
        $parsed = $null
    }

    $stamp = (Get-Date -AsUTC).ToString('yyyyMMddTHHmmssZ')
    $safeMod = ($modelOut -replace '[^A-Za-z0-9._-]','_')
    $outPath = Join-Path $resultsDir ("{0}-{1}-{2}.json" -f $stamp, $safeMod, $safeIterTag)

    $payload = [ordered]@{
        meta = [ordered]@{
            timestamp_utc      = $stamp
            tag                = $iterTag
            model_param        = $Model
            model_returned     = $modelOut
            api_version        = $ApiVersion
            endpoint_host      = ([Uri]$endpoint).Host
            temperature        = $Temperature
            seed               = $Seed
            iteration          = $iter
            iterations_total   = $Iterations
            doc_files          = $docFiles.Count
            prompt_chars       = $userMsg.Length
            prompt_tokens      = $usage.prompt_tokens
            completion_tokens  = $usage.completion_tokens
            total_tokens       = $usage.total_tokens
            duration_seconds   = [math]::Round($sw.Elapsed.TotalSeconds, 2)
            finish_reason      = $finish
            questions_path     = (Resolve-Path $QuestionsPath).Path
            git_commit         = (git -C $repoRoot rev-parse HEAD 2>$null)
            git_branch         = (git -C $repoRoot rev-parse --abbrev-ref HEAD 2>$null)
        }
        answers     = $parsed
        raw_content = $contentRaw
    }
    $payload | ConvertTo-Json -Depth 8 | Out-File -Encoding utf8 -FilePath $outPath
    Write-Host "  result -> $outPath"

    if ($Score) {
        $scorer = Join-Path $benchRoot 'score-qa-bench.ps1'
        if (Test-Path $scorer) {
            & $scorer -ResultPath $outPath -QuestionsPath $QuestionsPath
        } else {
            Write-Host "  (score-qa-bench.ps1 not found; skipping)" -ForegroundColor Yellow
        }
    }
}

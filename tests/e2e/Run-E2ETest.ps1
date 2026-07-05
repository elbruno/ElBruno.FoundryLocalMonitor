<#
.SYNOPSIS
    End-to-end test for Foundry Local Monitor.

.DESCRIPTION
    Verifies that the Foundry Local Monitor correctly detects:
      1. Service start  — Foundry Local web server comes up on a dynamic port
      2. Model load     — a model appears in GET /v1/models
      3. Model unload   — model list becomes empty again
      4. Service stop   — foundry service status reports stopped

    The test launches the FoundryLocalChat sample app (samples/FoundryLocalChat)
    which simulates the same scenario as the FoundryLocalProxy BYOK proxy
    (https://github.com/elbruno/ElBruno.CopilotHarness/tree/main/src/proxies/FoundryLocalProxy).

    With -LaunchMonitor the WPF systray app is also started so you can visually
    confirm every state transition in the UI while the script validates the
    underlying detection logic.

.PARAMETER ModelAlias
    Foundry Local model alias to load. Must be in the local cache or downloadable.
    Default: qwen2.5-coder-0.5b

.PARAMETER ServiceStartTimeout
    Seconds to wait for the Foundry Local service to start.
    Default: 90

.PARAMETER ModelLoadTimeout
    Seconds to wait for a model to appear in GET /v1/models.
    Default: 120

.PARAMETER LaunchMonitor
    Also launch the Foundry Local Monitor WPF app for visual E2E verification.
    The monitor is killed automatically at the end of the test.

.PARAMETER SkipBuild
    Skip dotnet build step (use existing binaries).

.EXAMPLE
    # Basic automated test (no GUI)
    .\Run-E2ETest.ps1

.EXAMPLE
    # Full visual E2E: monitor + chat app
    .\Run-E2ETest.ps1 -LaunchMonitor

.EXAMPLE
    # Use a different model
    .\Run-E2ETest.ps1 -ModelAlias phi-4-mini -LaunchMonitor
#>
[CmdletBinding()]
param(
    [string]  $ModelAlias          = 'qwen2.5-coder-0.5b',
    [int]     $ServiceStartTimeout = 90,
    [int]     $ModelLoadTimeout    = 120,
    [switch]  $LaunchMonitor,
    [switch]  $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Paths ─────────────────────────────────────────────────────────────────────
$RepoRoot    = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$SampleDir   = Join-Path $RepoRoot 'samples\FoundryLocalChat'
$MonitorSrc  = Join-Path $RepoRoot 'src\ElBruno.FoundryLocalMonitor'
$ResultsFile = Join-Path $PSScriptRoot 'last-run-results.txt'

# ── Helpers ───────────────────────────────────────────────────────────────────
$Script:Passed  = 0
$Script:Failed  = 0
$Script:Results = [System.Collections.Generic.List[string]]::new()

function Write-Header([string]$msg) {
    $line = '─' * ($msg.Length + 4)
    Write-Host "`n┌$line┐" -ForegroundColor Cyan
    Write-Host "│  $msg  │" -ForegroundColor Cyan
    Write-Host "└$line┘" -ForegroundColor Cyan
}

function Write-Step([string]$msg) {
    Write-Host "`n  ▶ $msg" -ForegroundColor White
}

function Assert-Pass([string]$label) {
    $Script:Passed++
    $entry = "  ✅ PASS  $label"
    Write-Host $entry -ForegroundColor Green
    $Script:Results.Add($entry)
}

function Assert-Fail([string]$label, [string]$detail = '') {
    $Script:Failed++
    $entry = "  ❌ FAIL  $label$(if ($detail) { " — $detail" })"
    Write-Host $entry -ForegroundColor Red
    $Script:Results.Add($entry)
}

function Wait-For([string]$Description, [int]$TimeoutSec, [scriptblock]$Condition) {
    Write-Host "     Waiting for: $Description (timeout: ${TimeoutSec}s)" -ForegroundColor DarkGray
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $interval = 3
    while ((Get-Date) -lt $deadline) {
        try {
            $result = & $Condition
            if ($result) { return $result }
        } catch { }
        $remaining = [int](($deadline - (Get-Date)).TotalSeconds)
        Write-Host "     ⏳ $remaining s remaining…" -ForegroundColor DarkGray -NoNewline
        Write-Host "`r" -NoNewline
        Start-Sleep -Seconds $interval
    }
    return $null
}

function Get-ServiceEndpoint {
    # Calls 'foundry service status' and extracts the base URL.
    # Returns null when service is not running.
    try {
        $raw = & foundry service status 2>&1 | Out-String
        if ($raw -match 'http://[\d.]+:\d+') {
            $full = $Matches[0]
            # Strip trailing path (e.g. /openai/status) — same as ExtractBaseUrl() in the monitor
            $uri = [System.Uri]$full
            return "$($uri.Scheme)://$($uri.Authority)"
        }
    } catch { }
    return $null
}

function Get-LoadedModels([string]$BaseUrl) {
    try {
        $resp = Invoke-RestMethod -Uri "$BaseUrl/v1/models" -Method Get -TimeoutSec 5
        return $resp.data
    } catch {
        return @()
    }
}

# ── Banner ────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '╔══════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║   Foundry Local Monitor — Automated E2E Test             ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host "  Model     : $ModelAlias"
Write-Host "  Visual    : $(if ($LaunchMonitor) { 'yes — monitor will launch' } else { 'no  — script-only validation' })"
Write-Host "  Repo root : $RepoRoot"
Write-Host ''

$StartTime = Get-Date

# ── PRE-CHECK: foundry CLI ────────────────────────────────────────────────────
Write-Header 'Pre-flight checks'

Write-Step 'Checking foundry CLI…'
try {
    $ver = & foundry --version 2>&1 | Select-Object -First 1
    Assert-Pass "foundry CLI found: $ver"
} catch {
    Assert-Fail 'foundry CLI not found' 'Install from https://aka.ms/foundrylocal'
    Write-Host "`n❌ Cannot continue without foundry CLI." -ForegroundColor Red
    exit 1
}

# ── BUILD ─────────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Header 'Building sample app'
    Write-Step 'dotnet build samples\FoundryLocalChat…'
    $buildOutput = & dotnet build $SampleDir -c Release --verbosity quiet 2>&1
    if ($LASTEXITCODE -eq 0) {
        Assert-Pass 'FoundryLocalChat built successfully'
    } else {
        Assert-Fail 'FoundryLocalChat build failed' ($buildOutput | Select-Object -Last 3 | Out-String).Trim()
        exit 1
    }
}

# ── STOP any existing service ─────────────────────────────────────────────────
Write-Header 'Clean state'
Write-Step 'Ensuring Foundry service is stopped before test…'
& foundry service stop 2>&1 | Out-Null
Start-Sleep -Seconds 2
$pre = Get-ServiceEndpoint
if ($null -eq $pre) {
    Assert-Pass 'Service confirmed stopped before test'
} else {
    Write-Host "  ⚠ Service still running at $pre — test will proceed but results may differ" -ForegroundColor Yellow
}

# ── LAUNCH MONITOR (optional) ─────────────────────────────────────────────────
$MonitorProc = $null
if ($LaunchMonitor) {
    Write-Header 'Launching Foundry Local Monitor'
    Write-Step 'Building monitor…'
    if (-not $SkipBuild) {
        & dotnet build $MonitorSrc -c Release --verbosity quiet 2>&1 | Out-Null
    }
    $monExe = Join-Path $MonitorSrc "bin\Release\net10.0-windows\ElBruno.FoundryLocalMonitor.exe"
    if (Test-Path $monExe) {
        $MonitorProc = Start-Process -FilePath $monExe -PassThru
        Write-Host "  ✔ Monitor started (PID $($MonitorProc.Id))" -ForegroundColor Green
        Start-Sleep -Seconds 3  # let it initialize before chat app starts
    } else {
        Write-Host "  ⚠ Monitor exe not found at expected path — skipping visual launch" -ForegroundColor Yellow
        Write-Host "    Path: $monExe"
    }
}

# ── LAUNCH SAMPLE APP ─────────────────────────────────────────────────────────
Write-Header 'Launching FoundryLocalChat (E2E driver)'
Write-Step "Starting FoundryLocalChat with model '$ModelAlias'…"

$env:FOUNDRY_DEMO_MODEL = $ModelAlias

$psi = [System.Diagnostics.ProcessStartInfo]@{
    FileName               = 'dotnet'
    Arguments              = "run --project `"$SampleDir`" --no-build -c Release"
    UseShellExecute        = $false
    RedirectStandardOutput = $true
    RedirectStandardError  = $true
    CreateNoWindow         = $true
}
$ChatProc = [System.Diagnostics.Process]::Start($psi)
Write-Host "  ✔ Chat app started (PID $($ChatProc.Id))" -ForegroundColor Green

# Collect output in background so the pipe buffer doesn't block the process
$ChatOutput = [System.Text.StringBuilder]::new()
$outputJob = [System.Threading.Tasks.Task]::Run([System.Action]{
    while ($true) {
        $line = $ChatProc.StandardOutput.ReadLine()
        if ($null -eq $line) { break }
        $ChatOutput.AppendLine($line) | Out-Null
    }
})

# ── TEST 1: SERVICE DETECTION ─────────────────────────────────────────────────
Write-Header 'Test 1 — Service detection'
Write-Step "Polling foundry service status (up to ${ServiceStartTimeout}s)…"

$endpoint = Wait-For -Description 'Foundry service to start' -TimeoutSec $ServiceStartTimeout -Condition {
    Get-ServiceEndpoint
}

if ($endpoint) {
    Assert-Pass "Service detected at $endpoint"
} else {
    Assert-Fail 'Service never started within timeout'
}

# ── TEST 2: MODEL LOAD DETECTION ─────────────────────────────────────────────
Write-Header 'Test 2 — Model load detection'
Write-Step "Polling GET /v1/models (up to ${ModelLoadTimeout}s)…"

$loadedModels = $null
if ($endpoint) {
    $loadedModels = Wait-For -Description "model '$ModelAlias' to appear in /v1/models" -TimeoutSec $ModelLoadTimeout -Condition {
        $models = Get-LoadedModels $endpoint
        if ($models -and $models.Count -gt 0) { return $models }
        return $null
    }
}

if ($loadedModels) {
    $ids = ($loadedModels | ForEach-Object { $_.id }) -join ', '
    Assert-Pass "Model(s) detected in /v1/models: $ids"

    # Verify the alias matches what we requested
    $aliasMatch = $loadedModels | Where-Object { $_.id -like "*$ModelAlias*" }
    if ($aliasMatch) {
        Assert-Pass "Loaded model matches requested alias '$ModelAlias'"
    } else {
        Assert-Fail "Loaded model ID does not contain '$ModelAlias'" "Got: $ids"
    }
} else {
    Assert-Fail 'No model appeared in /v1/models within timeout'
}

# ── TEST 3: WAIT FOR CHAT DEMO ────────────────────────────────────────────────
Write-Header 'Test 3 — Chat demo + model unload detection'
Write-Step 'Waiting for FoundryLocalChat to complete its demo…'

$exited = $ChatProc.WaitForExit(180_000)  # 3 minute max
if ($exited) {
    Write-Host "  ✔ Chat app exited (code $($ChatProc.ExitCode))" -ForegroundColor Green
} else {
    Write-Host '  ⚠ Chat app did not exit within 3 min — killing it' -ForegroundColor Yellow
    Stop-Process -Id $ChatProc.Id -Force
}

# ── TEST 4: MODEL UNLOAD DETECTION ───────────────────────────────────────────
Write-Step "Checking /v1/models after unload (up to 15s)…"

if ($endpoint) {
    $stillLoaded = Wait-For -Description 'model list to become empty' -TimeoutSec 15 -Condition {
        $models = Get-LoadedModels $endpoint
        if ($null -eq $models -or $models.Count -eq 0) { return $true }
        return $null
    }

    if ($stillLoaded) {
        Assert-Pass 'Model list empty after unload (/v1/models returned [])'
    } else {
        # May have already stopped entirely — that's also a pass
        $ep2 = Get-ServiceEndpoint
        if ($null -eq $ep2) {
            Assert-Pass 'Service stopped entirely — no models running (expected)'
        } else {
            $remaining = Get-LoadedModels $endpoint
            Assert-Fail 'Model still present after unload' "Remaining: $(($remaining | ForEach-Object { $_.id }) -join ', ')"
        }
    }
} else {
    Write-Host '  ⚠ Skipped — no endpoint was captured' -ForegroundColor Yellow
}

# ── TEST 5: SERVICE STOP DETECTION ───────────────────────────────────────────
Write-Step "Checking foundry service status after stop (up to 20s)…"

$stopped = Wait-For -Description 'Foundry service to stop' -TimeoutSec 20 -Condition {
    $ep = Get-ServiceEndpoint
    if ($null -eq $ep) { return $true }
    return $null
}

if ($stopped) {
    Assert-Pass 'Service confirmed stopped after app exit'
} else {
    Assert-Fail 'Service still running after chat app exited'
}

# ── CLEANUP ───────────────────────────────────────────────────────────────────
if ($MonitorProc -and -not $MonitorProc.HasExited) {
    Write-Step 'Stopping Foundry Local Monitor…'
    Stop-Process -Id $MonitorProc.Id -Force
    Write-Host '  ✔ Monitor stopped' -ForegroundColor Green
}

# ── RESULTS ───────────────────────────────────────────────────────────────────
$elapsed = ((Get-Date) - $StartTime).ToString('mm\:ss')

Write-Host ''
Write-Host '╔══════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║                    Test Results                          ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
$Script:Results | ForEach-Object { Write-Host $_ }
Write-Host ''

$total  = $Script:Passed + $Script:Failed
$color  = if ($Script:Failed -eq 0) { 'Green' } else { 'Red' }
$status = if ($Script:Failed -eq 0) { '✅ ALL PASSED' } else { "❌ $($Script:Failed) FAILED" }

Write-Host "  $status   ($($Script:Passed)/$total passed, elapsed: $elapsed)" -ForegroundColor $color
Write-Host ''

# Persist results for CI / history
$report = @"
E2E Test Run — $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Model     : $ModelAlias
Result    : $status
Elapsed   : $elapsed
Passed    : $($Script:Passed) / $total

$($Script:Results -join "`n")

Chat app output:
$($ChatOutput.ToString())
"@
$report | Set-Content $ResultsFile -Encoding UTF8
Write-Host "  Report saved to: $ResultsFile" -ForegroundColor DarkGray
Write-Host ''

exit $(if ($Script:Failed -eq 0) { 0 } else { 1 })

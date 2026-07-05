<#
.SYNOPSIS
    End-to-end test for Foundry Local Monitor.

.DESCRIPTION
    Verifies that the Foundry Local Monitor correctly detects:
      1. Service start  -- Foundry Local web server comes up on a dynamic port
      2. Model load     -- a model appears in GET /v1/models
      3. Model unload   -- model list becomes empty again
      4. Service stop   -- foundry service status reports stopped

    Run with -LaunchMonitor to also start the WPF systray app so you can
    visually confirm every state transition in the UI.

.PARAMETER ModelAlias
    Foundry Local model alias to load. Must be in the local cache or downloadable.
    Default: qwen2.5-coder-0.5b

.PARAMETER ServiceStartTimeout
    Seconds to wait for the Foundry Local service to start. Default: 90

.PARAMETER ModelLoadTimeout
    Seconds to wait for a model to appear in GET /v1/models. Default: 120

.PARAMETER LaunchMonitor
    Also launch the Foundry Local Monitor WPF systray app for visual E2E.
    The monitor is killed automatically at the end of the test.

.PARAMETER SkipBuild
    Skip dotnet build step (use existing binaries).

.EXAMPLE
    .\Run-E2ETest.ps1 -LaunchMonitor

.EXAMPLE
    .\Run-E2ETest.ps1 -ModelAlias phi-4-mini -LaunchMonitor
#>
[CmdletBinding()]
param(
    [string] $ModelAlias          = 'qwen2.5-coder-0.5b',
    [int]    $ServiceStartTimeout = 90,
    [int]    $ModelLoadTimeout    = 720,
    [switch] $LaunchMonitor,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$StartTime = Get-Date

# ---- Paths ------------------------------------------------------------------
$RepoRoot    = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$SampleDir   = Join-Path $RepoRoot 'samples\FoundryLocalChat'
$MonitorSrc  = Join-Path $RepoRoot 'src\ElBruno.FoundryLocalMonitor'
$ResultsFile = Join-Path $PSScriptRoot 'last-run-results.txt'

# ---- State ------------------------------------------------------------------
$Script:Passed  = 0
$Script:Failed  = 0
$Script:Results = [System.Collections.Generic.List[string]]::new()

# ---- Helpers ----------------------------------------------------------------
function Write-Banner([string]$title) {
    $bar = '=' * 60
    Write-Host ""
    Write-Host $bar -ForegroundColor Cyan
    Write-Host "  $title" -ForegroundColor Cyan
    Write-Host $bar -ForegroundColor Cyan
    Write-Host ""
}

function Write-Section([string]$msg) {
    Write-Host ""
    Write-Host "--- $msg ---" -ForegroundColor Yellow
}

function Write-Step([string]$msg) {
    Write-Host "  >> $msg" -ForegroundColor White
}

function Assert-Pass([string]$label) {
    $Script:Passed++
    $entry = "  [PASS] $label"
    Write-Host $entry -ForegroundColor Green
    $Script:Results.Add($entry)
}

function Assert-Fail([string]$label, [string]$detail) {
    $Script:Failed++
    $entry = "  [FAIL] $label"
    if ($detail) { $entry += " -- $detail" }
    Write-Host $entry -ForegroundColor Red
    $Script:Results.Add($entry)
}

function Wait-For([string]$Description, [int]$TimeoutSec, [scriptblock]$Condition) {
    Write-Host "     Waiting: $Description (max ${TimeoutSec}s)" -ForegroundColor DarkGray
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $result = & $Condition
            if ($result) { return $result }
        } catch { }
        $remaining = [int](($deadline - (Get-Date)).TotalSeconds)
        Write-Host "     $remaining s remaining...`r" -NoNewline -ForegroundColor DarkGray
        Start-Sleep -Seconds 3
    }
    Write-Host ""
    return $null
}

function Get-ServiceEndpoint {
    try {
        # Fast path: look for Inference.Service.Agent (the foundry daemon) directly.
        # This avoids calling 'foundry service status' which blocks for 5-10s when
        # the daemon is busy doing EP autoregistration or model loading.
        $proc = Get-Process -Name "Inference.Service.Agent" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($proc) {
            $conn = Get-NetTCPConnection -OwningProcess $proc.Id -State Listen `
                        -LocalAddress 127.0.0.1 -ErrorAction SilentlyContinue |
                    Select-Object -First 1
            if ($conn) {
                return "http://127.0.0.1:$($conn.LocalPort)"
            }
        }
    } catch { }

    # Slow fallback: parse 'foundry service status' output (may take 5-10s when busy)
    try {
        $raw = & foundry service status 2>&1 | Out-String
        if ($raw -match 'http://[\d.]+:\d+') {
            $full = $Matches[0]
            $uri  = [System.Uri]$full
            return "$($uri.Scheme)://$($uri.Authority)"
        }
    } catch { }
    return $null
}

function Get-LoadedModels([string]$BaseUrl) {
    try {
        $resp = Invoke-RestMethod -Uri "$BaseUrl/v1/models" -Method Get -TimeoutSec 5
        # PS5.1: single-element JSON arrays are unwrapped to PSCustomObject, losing .Count.
        # Wrap in @() and filter nulls to get a real array in all cases.
        $data = @($resp.data) | Where-Object { $null -ne $_ }
        return $data
    } catch {
        return @()
    }
}

# ---- Banner -----------------------------------------------------------------
Write-Banner "Foundry Local Monitor -- Automated E2E Test"
Write-Host "  Model   : $ModelAlias"
$visualMode = if ($LaunchMonitor) { "YES -- monitor WPF app will launch" } else { "NO  -- headless validation only" }
Write-Host "  Monitor : $visualMode"
Write-Host "  Root    : $RepoRoot"
Write-Host ""

# ---- Pre-flight: foundry CLI ------------------------------------------------
Write-Section "Pre-flight checks"

Write-Step "Checking foundry CLI..."
try {
    $ver = & foundry --version 2>&1 | Select-Object -First 1
    Assert-Pass "foundry CLI found: $ver"
} catch {
    Assert-Fail "foundry CLI not found" "Install from https://aka.ms/foundrylocal"
    Write-Host ""
    Write-Host "Cannot continue without foundry CLI." -ForegroundColor Red
    exit 1
}

# ---- Build ------------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Section "Building sample app"
    Write-Step "dotnet build samples\FoundryLocalChat..."
    $buildOut = & dotnet build $SampleDir -c Release --verbosity quiet 2>&1
    if ($LASTEXITCODE -eq 0) {
        Assert-Pass "FoundryLocalChat built successfully"
    } else {
        Assert-Fail "FoundryLocalChat build failed" ($buildOut | Select-Object -Last 3 | Out-String).Trim()
        exit 1
    }
}

# ---- Clean state ------------------------------------------------------------
Write-Section "Ensuring clean state"
Write-Step "Stopping any existing Foundry service..."
& foundry service stop 2>&1 | Out-Null
Start-Sleep -Seconds 2
$pre = Get-ServiceEndpoint
if ($null -eq $pre) {
    Assert-Pass "Service confirmed stopped before test"
} else {
    Write-Host "  [WARN] Service still running at $pre -- continuing anyway" -ForegroundColor Yellow
}

# ---- Launch Monitor (optional) ----------------------------------------------
$MonitorProc = $null
if ($LaunchMonitor) {
    Write-Section "Launching Foundry Local Monitor (WPF systray)"
    if (-not $SkipBuild) {
        Write-Step "Building monitor..."
        & dotnet build $MonitorSrc -c Release --verbosity quiet 2>&1 | Out-Null
    }
    $monExe = Join-Path $MonitorSrc "bin\Release\net10.0-windows\ElBruno.FoundryLocalMonitor.exe"
    if (Test-Path $monExe) {
        $MonitorProc = Start-Process -FilePath $monExe -PassThru
        Write-Host "  [OK] Monitor started (PID $($MonitorProc.Id))" -ForegroundColor Green
        Write-Host "       --> Check your system tray for the Foundry icon" -ForegroundColor Cyan
        Start-Sleep -Seconds 3
    } else {
        Write-Host "  [WARN] Monitor exe not found: $monExe" -ForegroundColor Yellow
        Write-Host "         Run without -SkipBuild to build it first." -ForegroundColor Yellow
    }
}

# ---- Launch sample app ------------------------------------------------------
Write-Section "Launching FoundryLocalChat (E2E driver app)"
Write-Step "Starting FoundryLocalChat with model '$ModelAlias'..."

$env:FOUNDRY_DEMO_MODEL = $ModelAlias

$psi = [System.Diagnostics.ProcessStartInfo]@{
    FileName               = 'dotnet'
    Arguments              = "run --project `"$SampleDir`" --no-build -c Release"
    UseShellExecute        = $false
    RedirectStandardOutput = $true
    RedirectStandardError  = $true
    CreateNoWindow         = $true
}
$ChatProc   = [System.Diagnostics.Process]::Start($psi)
$ChatOutput = [System.Text.StringBuilder]::new()
$ChatError  = [System.Text.StringBuilder]::new()

# Drain stdout AND stderr in background so pipes never block the process.
# NOTE: Do NOT call Write-Host from Task.Run threads in PS5.1 -- it causes
# console-lock deadlocks with the main thread. Collect output silently here;
# the main thread prints a summary after the process exits.
$null = [System.Threading.Tasks.Task]::Run([System.Action]{
    while ($true) {
        $line = $ChatProc.StandardOutput.ReadLine()
        if ($null -eq $line) { break }
        $ChatOutput.AppendLine($line) | Out-Null
    }
})
$null = [System.Threading.Tasks.Task]::Run([System.Action]{
    while ($true) {
        $line = $ChatProc.StandardError.ReadLine()
        if ($null -eq $line) { break }
        $ChatError.AppendLine($line) | Out-Null
    }
})

Write-Host "  [OK] Chat app started (PID $($ChatProc.Id))" -ForegroundColor Green
if ($LaunchMonitor) {
    Write-Host "       --> Watch the systray icon -- it should change as the app progresses" -ForegroundColor Cyan
}

# ============================================================================
# TEST 1 -- Service detection
# ============================================================================
Write-Section "TEST 1 -- Service detection"
Write-Step "Polling 'foundry service status' (up to ${ServiceStartTimeout}s)..."

$endpoint = Wait-For -Description "Foundry service to start" -TimeoutSec $ServiceStartTimeout -Condition {
    Get-ServiceEndpoint
}

if ($endpoint) {
    $Script:endpoint = $endpoint
    Assert-Pass "Service detected at $endpoint"
    if ($LaunchMonitor) {
        Write-Host "       --> Monitor should now show: $endpoint" -ForegroundColor Cyan
    }
} else {
    Assert-Fail "Service never started within ${ServiceStartTimeout}s" ""
}

# ============================================================================
# TEST 2 -- Model load detection
# ============================================================================
Write-Section "TEST 2 -- Model load detection"
Write-Step "Polling GET /v1/models (up to ${ModelLoadTimeout}s)..."

$loadedModels = $null
$loadedModels = Wait-For -Description "model '$ModelAlias' to appear in /v1/models" -TimeoutSec $ModelLoadTimeout -Condition {
    # Re-detect service endpoint on every poll -- the port can change after
    # initial detection (SDK may restart the daemon with a different port).
    $liveEp = Get-ServiceEndpoint
    if ($liveEp -and $liveEp -ne $endpoint) {
        $Script:endpoint = $liveEp
    }
    if (-not $Script:endpoint) { return $null }
    $models = Get-LoadedModels $Script:endpoint
    # PS5.1 returns 1-element arrays as PSCustomObject (no .Count).
    # Use plain truthy check: empty array @() is $false, object/array is $true.
    if ($models) { return $models }
    return $null
}

if ($loadedModels) {
    $ids = ($loadedModels | ForEach-Object { $_.id }) -join ', '
    Assert-Pass "Model(s) detected in /v1/models: $ids"
    $aliasMatch = $loadedModels | Where-Object { $_.id -like "*$ModelAlias*" }
    if ($aliasMatch) {
        Assert-Pass "Model ID matches requested alias '$ModelAlias'"
    } else {
        Assert-Fail "Model ID does not contain '$ModelAlias'" "Got: $ids"
    }
    if ($LaunchMonitor) {
        Write-Host "       --> Monitor should now show the model name + device" -ForegroundColor Cyan
    }
} else {
    Assert-Fail "No model appeared in /v1/models within ${ModelLoadTimeout}s" ""
}

# ============================================================================
# TEST 3 -- Wait for chat demo to complete
# ============================================================================
Write-Section "TEST 3 -- Chat demo running (waiting for app to complete)"
Write-Step "Waiting for FoundryLocalChat to finish its automated chat demo..."

$exited = $ChatProc.WaitForExit(600000)
if ($exited) {
    Write-Host "  [OK] Chat app exited (code $($ChatProc.ExitCode))" -ForegroundColor Green
} else {
    Write-Host "  [WARN] Chat app did not exit in 10min -- killing it" -ForegroundColor Yellow
    Stop-Process -Id $ChatProc.Id -Force -ErrorAction SilentlyContinue
}

# ============================================================================
# TEST 4 -- Model unload detection
# ============================================================================
Write-Section "TEST 4 -- Model unload detection"
Write-Step "Checking /v1/models is empty after unload (up to 15s)..."

if ($Script:endpoint) {
    $unloaded = Wait-For -Description "model list to become empty" -TimeoutSec 45 -Condition {
        $models = Get-LoadedModels $Script:endpoint
        # PS5.1 returns 1-element arrays as PSCustomObject (no .Count).
        # Models empty when: Get-LoadedModels returns @() (empty = $false in PS5.1)
        if (-not $models) { return $true }
        return $null
    }

    if ($unloaded) {
        Assert-Pass "Model list empty after unload (/v1/models returned [])"
    } else {
        $ep2 = Get-ServiceEndpoint
        if ($null -eq $ep2) {
            Assert-Pass "Service stopped entirely -- no models running (expected)"
        } else {
            $remaining = Get-LoadedModels $Script:endpoint
            $remainIds = ($remaining | ForEach-Object { $_.id }) -join ', '
            Assert-Fail "Model still present after unload" "Remaining: $remainIds"
        }
    }
    if ($LaunchMonitor) {
        Write-Host "       --> Monitor should now show: no models loaded" -ForegroundColor Cyan
    }
} else {
    Write-Host "  [SKIP] No endpoint captured -- skipping unload check" -ForegroundColor Yellow
}

# ============================================================================
# TEST 5 -- Service stop detection
# ============================================================================
Write-Section "TEST 5 -- Service stop detection"
Write-Step "Checking 'foundry service status' is stopped (up to 60s)..."

$stopped = Wait-For -Description "Foundry service to stop" -TimeoutSec 60 -Condition {
    $ep = Get-ServiceEndpoint
    if ($null -eq $ep) { return $true }
    return $null
}

if ($stopped) {
    Assert-Pass "Service confirmed stopped after app exit"
    if ($LaunchMonitor) {
        Write-Host "       --> Monitor should now show: service stopped" -ForegroundColor Cyan
    }
} else {
    Assert-Fail "Service still running after chat app exited" ""
}

# ---- Cleanup ----------------------------------------------------------------
if ($MonitorProc -and -not $MonitorProc.HasExited) {
    Write-Step "Stopping Foundry Local Monitor..."
    Stop-Process -Id $MonitorProc.Id -Force -ErrorAction SilentlyContinue
    Write-Host "  [OK] Monitor stopped" -ForegroundColor Green
}

# Force-stop the Foundry service in case the sample app crashed without cleanup
$ep = Get-ServiceEndpoint
if ($ep) {
    Write-Step "Force-stopping Foundry service (app may have crashed)..."
    & foundry service stop 2>&1 | Out-Null
    Write-Host "  [OK] Service stopped via CLI" -ForegroundColor Green
}

# ---- Results ----------------------------------------------------------------
$elapsed = ((Get-Date) - $StartTime).ToString('mm\:ss')
$total   = $Script:Passed + $Script:Failed
$ok      = $Script:Failed -eq 0
$status  = if ($ok) { "ALL PASSED" } else { "$($Script:Failed) FAILED" }
$color   = if ($ok) { 'Green' } else { 'Red' }

Write-Host ""
Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host "  Test Results" -ForegroundColor Cyan
Write-Host ("=" * 60) -ForegroundColor Cyan
$Script:Results | ForEach-Object { Write-Host $_ }
Write-Host ""
Write-Host "  $status  ($($Script:Passed)/$total passed, elapsed: $elapsed)" -ForegroundColor $color
Write-Host ""

$report = @"
E2E Test Run -- $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Model   : $ModelAlias
Result  : $status
Elapsed : $elapsed
Passed  : $($Script:Passed) / $total

$($Script:Results -join "`r`n")

--- Chat app stdout ---
$($ChatOutput.ToString())

--- Chat app stderr ---
$($ChatError.ToString())
"@
$report | Set-Content $ResultsFile -Encoding UTF8
Write-Host "  Report saved: $ResultsFile" -ForegroundColor DarkGray
Write-Host ""

exit $(if ($ok) { 0 } else { 1 })

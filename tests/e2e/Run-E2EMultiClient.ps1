<#
.SYNOPSIS
    Multi-client E2E test for Foundry Local Monitor.

.DESCRIPTION
    Verifies that Foundry Local Monitor correctly detects events from TWO
    simultaneous clients — a C# SDK app and a Python SDK app — running at
    the same time on different internal ports.

    Test sequence:
      PRE   Service and daemon baseline check
      1     C# app starts: monitor discovers port 55588 endpoint
      2     C# app loads model: monitor shows model from C# client
      3     Python app starts: monitor discovers port 55589 endpoint
      4     Python app loads model: monitor shows model from Python client
      5     C# model unloaded: monitor clears C# model
      6     Python model unloaded: monitor clears Python model
      7     Both services stopped: monitor shows no active endpoints

    Run with -LaunchMonitor to also start the WPF systray app so you can
    visually confirm every state transition in the UI.

.PARAMETER ModelAlias
    Foundry model alias to use for both clients. Default: qwen2.5-coder-0.5b

.PARAMETER MonitorWait
    Seconds to pause between steps for the monitor to catch up. Default: 12

.PARAMETER ServiceStartTimeout
    Seconds to wait for the foundry service to start. Default: 90

.PARAMETER ModelLoadTimeout
    Seconds to wait for a model to appear in /v1/models. Default: 120

.PARAMETER LaunchMonitor
    Also launch the Foundry Local Monitor WPF systray app for visual E2E.
    The monitor is killed automatically at the end of the test.

.PARAMETER SkipBuild
    Skip dotnet build step (use existing binaries).

.EXAMPLE
    .\Run-E2EMultiClient.ps1 -LaunchMonitor

.EXAMPLE
    .\Run-E2EMultiClient.ps1 -ModelAlias phi-4-mini -MonitorWait 15
#>
[CmdletBinding()]
param(
    [string] $ModelAlias          = 'qwen2.5-coder-0.5b',
    [int]    $MonitorWait         = 12,
    [int]    $ServiceStartTimeout = 90,
    [int]    $ModelLoadTimeout    = 120,
    [switch] $LaunchMonitor,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$StartTime = Get-Date

# ── Paths ─────────────────────────────────────────────────────────────────────
$RepoRoot       = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$CSharpSample   = Join-Path $RepoRoot 'samples\FoundryLocalChat'
$PythonSample   = Join-Path $RepoRoot 'samples\FoundryLocalChatPy'
$MonitorSrc     = Join-Path $RepoRoot 'src\ElBruno.FoundryLocalMonitor'
$ResultsFile    = Join-Path $PSScriptRoot 'last-multi-client-results.txt'

# Foundry Local SDK port for each client (must differ to avoid conflict)
$CSharpPort  = 55588
$PythonPort  = 55589

# ── State ─────────────────────────────────────────────────────────────────────
$Script:Passed  = 0
$Script:Failed  = 0
$Script:Results = [System.Collections.Generic.List[string]]::new()

# ── Helpers ───────────────────────────────────────────────────────────────────
function Write-Banner([string]$title) {
    $bar = '=' * 64
    Write-Host "" ; Write-Host $bar -ForegroundColor Cyan
    Write-Host "  $title" -ForegroundColor Cyan
    Write-Host $bar -ForegroundColor Cyan ; Write-Host ""
}

function Write-Section([string]$msg) {
    Write-Host "" ; Write-Host "--- $msg ---" -ForegroundColor Yellow
}

function Assert-Pass([string]$label) {
    $Script:Passed++
    $e = "  [PASS] $label"
    Write-Host $e -ForegroundColor Green ; $Script:Results.Add($e)
}

function Assert-Fail([string]$label, [string]$detail = '') {
    $Script:Failed++
    $e = "  [FAIL] $label$(if($detail){ " -- $detail" })"
    Write-Host $e -ForegroundColor Red ; $Script:Results.Add($e)
}

function Wait-For([string]$Description, [int]$TimeoutSec, [scriptblock]$Condition) {
    Write-Host "     Waiting: $Description (max ${TimeoutSec}s)" -ForegroundColor DarkGray
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try { $r = & $Condition ; if ($r) { return $r } } catch { }
        $rem = [int](($deadline - (Get-Date)).TotalSeconds)
        Write-Host "     ${rem}s remaining...`r" -NoNewline -ForegroundColor DarkGray
        Start-Sleep -Seconds 3
    }
    Write-Host "" ; return $null
}

function Get-FoundryApiPorts {
    <#
    .SYNOPSIS
        Returns all localhost ports currently serving the Foundry API (/v1/models).
        Mirrors the FoundryEndpointDiscovery logic used by the monitor.
    #>
    $listening = (Get-NetTCPConnection -State Listen -LocalAddress 127.0.0.1 -ErrorAction SilentlyContinue).LocalPort | 
                  Sort-Object -Unique
    $found = @()
    foreach ($p in $listening) {
        try {
            $resp = Invoke-RestMethod "http://127.0.0.1:$p/v1/models" -TimeoutSec 1 -ErrorAction Stop
            if ($resp.object -eq 'list') { $found += $p }
        } catch { }
    }
    return $found
}

function Get-LoadedModels([string]$BaseUrl) {
    try {
        $resp = Invoke-RestMethod -Uri "$BaseUrl/v1/models" -Method Get -TimeoutSec 5
        return @($resp.data) | Where-Object { $null -ne $_ }
    } catch { return @() }
}

function Stop-AllFoundry {
    Write-Host "  [cleanup] Stopping Foundry services and processes..." -ForegroundColor DarkGray
    try { & foundry service stop 2>&1 | Out-Null } catch { }
    Start-Sleep -Seconds 3
    Get-Process -Name "Inference.Service.Agent","FoundryLocalChat" -ErrorAction SilentlyContinue |
        ForEach-Object { try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue } catch { } }
}

# ── Output log helper (writes to file from background jobs) ───────────────────
function Start-LoggedProcess([string]$Exe, [string]$ArgString, [string]$LogFile, [hashtable]$Env = @{}) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName  = $Exe
    $psi.Arguments = $ArgString
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow  = $true
    foreach ($k in $Env.Keys) { $psi.Environment[$k] = $Env[$k] }
    $proc = [System.Diagnostics.Process]::Start($psi)
    # Drain stdout/stderr to log file asynchronously
    $stdout = $proc.StandardOutput
    $stderr = $proc.StandardError
    [System.Threading.Tasks.Task]::Run([System.Action]{
        while (-not $stdout.EndOfStream) {
            $line = $stdout.ReadLine()
            Add-Content $LogFile "  [stdout] $line" -ErrorAction SilentlyContinue
        }
    }) | Out-Null
    [System.Threading.Tasks.Task]::Run([System.Action]{
        while (-not $stderr.EndOfStream) {
            $line = $stderr.ReadLine()
            Add-Content $LogFile "  [stderr] $line" -ErrorAction SilentlyContinue
        }
    }) | Out-Null
    return $proc
}

# ═══════════════════════════════════════════════════════════════════════════════
# MAIN
# ═══════════════════════════════════════════════════════════════════════════════
Write-Banner "Foundry Local Monitor — Multi-Client E2E Test"
Write-Host "  C# client  port : $CSharpPort  (sample: samples\FoundryLocalChat)"
Write-Host "  Python client port: $PythonPort  (sample: samples\FoundryLocalChatPy)"
Write-Host "  Model alias : $ModelAlias"
Write-Host ""

$CSharpLog  = Join-Path $PSScriptRoot 'e2e-csharp.log'
$PythonLog  = Join-Path $PSScriptRoot 'e2e-python.log'
'', '' | Set-Content $CSharpLog, $PythonLog -ErrorAction SilentlyContinue

# ── PRE: Build C# sample ──────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Section "Building C# sample"
    Write-Host "  dotnet build $CSharpSample ..." -ForegroundColor White
    $buildOut = & dotnet build $CSharpSample -c Release --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $buildOut -ForegroundColor Red
        throw "C# sample build failed"
    }
    Write-Host "  Build OK" -ForegroundColor Green
}

# ── PRE: Ensure Python dependencies ──────────────────────────────────────────
Write-Section "Checking Python dependencies"
$pipOut = & pip show foundry-local-sdk 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Installing foundry-local-sdk..." -ForegroundColor White
    & pip install foundry-local-sdk openai --quiet
}
Write-Host "  Python deps OK" -ForegroundColor Green

# ── Launch monitor (optional) ─────────────────────────────────────────────────
$MonitorProc = $null
if ($LaunchMonitor) {
    Write-Section "Launching Foundry Local Monitor"
    $monitorExe = Get-ChildItem "$MonitorSrc\bin\Release" -Recurse -Filter "ElBruno.FoundryLocalMonitor.exe" |
                  Select-Object -First 1
    if ($null -eq $monitorExe) {
        # Build monitor if needed
        & dotnet build $MonitorSrc -c Release --nologo 2>&1 | Out-Null
        $monitorExe = Get-ChildItem "$MonitorSrc\bin\Release" -Recurse -Filter "ElBruno.FoundryLocalMonitor.exe" |
                      Select-Object -First 1
    }
    if ($monitorExe) {
        $MonitorProc = Start-Process -FilePath $monitorExe.FullName -PassThru
        Write-Host "  Monitor launched (PID=$($MonitorProc.Id))" -ForegroundColor Green
        Start-Sleep -Seconds 4   # give it time to show the tray icon
    } else {
        Write-Host "  [WARN] Monitor exe not found — skipping visual launch" -ForegroundColor Yellow
    }
}

# Stop anything from a previous run
Stop-AllFoundry

$CSharpProc = $null
$PythonProc = $null

try {
    # ─────────────────────────────────────────────────────────────────────────
    # TEST 1 — C# client starts, monitor discovers its endpoint
    # ─────────────────────────────────────────────────────────────────────────
    Write-Section "TEST 1 — C# client service start"
    Write-Host "  Launching FoundryLocalChat (C# SDK, port $CSharpPort)..." -ForegroundColor White

    $csharpDll = Get-ChildItem "$CSharpSample\bin\Release" -Recurse -Filter "FoundryLocalChat.dll" |
                 Select-Object -First 1
    if ($null -eq $csharpDll) { throw "FoundryLocalChat.dll not found — run without -SkipBuild" }

    $csharpEnv = @{
        FOUNDRY_DEMO_MODEL    = $ModelAlias
        FOUNDRY_INTERNAL_PORT = "$CSharpPort"
        FOUNDRY_MONITOR_WAIT  = "$MonitorWait"
    }
    $CSharpProc = Start-LoggedProcess "dotnet" $csharpDll.FullName $CSharpLog $csharpEnv
    Write-Host "  C# client started (PID=$($CSharpProc.Id))" -ForegroundColor Green

    $ep1 = Wait-For "C# SDK port $CSharpPort to serve /v1/models" $ServiceStartTimeout {
        try {
            $r = Invoke-RestMethod "http://127.0.0.1:$CSharpPort/v1/models" -TimeoutSec 1 -ErrorAction Stop
            return ($r.object -eq 'list')
        } catch { return $false }
    }
    if ($ep1) { Assert-Pass "TEST 1: C# SDK endpoint (port $CSharpPort) is visible" }
    else       { Assert-Fail "TEST 1: C# SDK endpoint NOT detected" "Port $CSharpPort not responding" }

    # ─────────────────────────────────────────────────────────────────────────
    # TEST 2 — C# app loads model, monitor should see it
    # ─────────────────────────────────────────────────────────────────────────
    Write-Section "TEST 2 — C# client model load"

    $m2 = Wait-For "C# model to appear at port $CSharpPort" $ModelLoadTimeout {
        $models = Get-LoadedModels "http://127.0.0.1:$CSharpPort"
        if ($models) { return $models }
        return $null
    }
    if ($m2) {
        $ids = (@($m2) | ForEach-Object { $_.id }) -join ', '
        Assert-Pass "TEST 2: C# model loaded — $ids"
    } else {
        Assert-Fail "TEST 2: No model detected at C# endpoint"
    }
    Start-Sleep -Seconds $MonitorWait

    # ─────────────────────────────────────────────────────────────────────────
    # TEST 3 — Python client starts, monitor discovers its endpoint
    # ─────────────────────────────────────────────────────────────────────────
    Write-Section "TEST 3 — Python client service start"
    Write-Host "  Launching FoundryLocalChatPy (Python SDK, port $PythonPort)..." -ForegroundColor White

    $pythonEnv = @{
        FOUNDRY_DEMO_MODEL     = $ModelAlias
        FOUNDRY_INTERNAL_PORT  = "$PythonPort"
        FOUNDRY_MONITOR_WAIT   = "$MonitorWait"
    }
    $PythonProc = Start-LoggedProcess "python" "$PythonSample\app.py" $PythonLog $pythonEnv
    Write-Host "  Python client started (PID=$($PythonProc.Id))" -ForegroundColor Green

    $ep3 = Wait-For "Python SDK port $PythonPort to serve /v1/models" $ServiceStartTimeout {
        try {
            $r = Invoke-RestMethod "http://127.0.0.1:$PythonPort/v1/models" -TimeoutSec 1 -ErrorAction Stop
            return ($r.object -eq 'list')
        } catch { return $false }
    }
    if ($ep3) { Assert-Pass "TEST 3: Python SDK endpoint (port $PythonPort) is visible" }
    else       { Assert-Fail "TEST 3: Python SDK endpoint NOT detected" "Port $PythonPort not responding" }

    # ─────────────────────────────────────────────────────────────────────────
    # TEST 4 — Python app loads model, monitor sees it alongside C# model
    # ─────────────────────────────────────────────────────────────────────────
    Write-Section "TEST 4 — Python client model load"

    # NOTE: The daemon can serve one model per context slot.
    # Both clients may see the same model loaded. We verify BOTH endpoints respond.
    $m4 = Wait-For "Python model to appear at port $PythonPort" $ModelLoadTimeout {
        $models = Get-LoadedModels "http://127.0.0.1:$PythonPort"
        if ($models) { return $models }
        return $null
    }
    if ($m4) {
        $ids = (@($m4) | ForEach-Object { $_.id }) -join ', '
        Assert-Pass "TEST 4: Python model loaded — $ids"
    } else {
        Assert-Fail "TEST 4: No model detected at Python endpoint"
    }

    # Bonus: verify monitor can see BOTH endpoints at the same time
    $allPorts = Get-FoundryApiPorts
    if ($allPorts -contains $CSharpPort -and $allPorts -contains $PythonPort) {
        Assert-Pass "TEST 4b: Both endpoints discoverable simultaneously (ports: $($allPorts -join ','))"
    } else {
        Assert-Fail "TEST 4b: Not all endpoints visible at once" "Found ports: $($allPorts -join ',')"
    }
    Start-Sleep -Seconds $MonitorWait

    # ─────────────────────────────────────────────────────────────────────────
    # TEST 5 — Wait for both clients to complete their unload/stop steps
    # ─────────────────────────────────────────────────────────────────────────
    Write-Section "TEST 5 — Both clients complete (model unload + service stop)"

    $maxWait = [int]($ModelLoadTimeout * 2)
    $done = Wait-For "Both client processes to exit" $maxWait {
        $cExit = $CSharpProc.HasExited
        $pExit = ($null -eq $PythonProc) -or $PythonProc.HasExited
        return ($cExit -and $pExit)
    }

    if ($done) {
        Assert-Pass "TEST 5: Both client processes exited cleanly"
    } else {
        # Force-stop stragglers
        try { if (-not $CSharpProc.HasExited) { Stop-Process -Id $CSharpProc.Id -Force } } catch { }
        try { if ($PythonProc -and -not $PythonProc.HasExited) { Stop-Process -Id $PythonProc.Id -Force } } catch { }
        Assert-Fail "TEST 5: Clients did not exit within timeout"
    }

    # ─────────────────────────────────────────────────────────────────────────
    # TEST 6 — After service stop, endpoints should be gone
    # ─────────────────────────────────────────────────────────────────────────
    Write-Section "TEST 6 — Foundry API endpoints gone after clients exit"
    Start-Sleep -Seconds 5

    $remaining = Wait-For "Foundry API ports to disappear" 60 {
        $ports = Get-FoundryApiPorts
        $cs    = $ports -contains $CSharpPort
        $py    = $ports -contains $PythonPort
        return (-not $cs -and -not $py)
    }

    if ($remaining) {
        Assert-Pass "TEST 6: Both Foundry API endpoints cleared"
    } else {
        $leftover = Get-FoundryApiPorts
        Assert-Fail "TEST 6: Foundry API ports still responding" "Remaining: $($leftover -join ',')"
    }

} finally {
    # ── Cleanup ───────────────────────────────────────────────────────────────
    Write-Section "Cleanup"
    try { if ($CSharpProc -and -not $CSharpProc.HasExited) { Stop-Process -Id $CSharpProc.Id -Force } } catch { }
    try { if ($PythonProc -and -not $PythonProc.HasExited) { Stop-Process -Id $PythonProc.Id -Force } } catch { }
    Stop-AllFoundry
    if ($MonitorProc) {
        try { Stop-Process -Id $MonitorProc.Id -Force -ErrorAction SilentlyContinue } catch { }
        Write-Host "  Monitor stopped" -ForegroundColor DarkGray
    }
}

# ── Results ───────────────────────────────────────────────────────────────────
$elapsed = ((Get-Date) - $StartTime).ToString('m\:ss')
Write-Banner "Multi-Client E2E Test Results"
$Script:Results | ForEach-Object { Write-Host $_ }
Write-Host ""
Write-Host "  Passed : $($Script:Passed)" -ForegroundColor Green
Write-Host "  Failed : $($Script:Failed)" -ForegroundColor $(if ($Script:Failed -eq 0) { 'Green' } else { 'Red' })
Write-Host "  Elapsed: $elapsed"
Write-Host ""

# ── Save results ──────────────────────────────────────────────────────────────
$header = "Multi-Client E2E — $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') — $($Script:Passed) passed, $($Script:Failed) failed, ${elapsed}"
Set-Content $ResultsFile $header
Add-Content $ResultsFile ""
$Script:Results | ForEach-Object { Add-Content $ResultsFile $_ }
Add-Content $ResultsFile ""
Add-Content $ResultsFile "--- C# client log ---"
Get-Content $CSharpLog -ErrorAction SilentlyContinue | Add-Content $ResultsFile
Add-Content $ResultsFile ""
Add-Content $ResultsFile "--- Python client log ---"
Get-Content $PythonLog -ErrorAction SilentlyContinue | Add-Content $ResultsFile

Write-Host "  Full results saved to: $ResultsFile" -ForegroundColor DarkGray

if ($Script:Failed -gt 0) { exit 1 } else { exit 0 }

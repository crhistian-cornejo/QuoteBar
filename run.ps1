# QuoteBar - Dev Run Script (with live logs)
# Similar to "bun run dev" - rebuilds and shows logs in terminal

param(
    [switch]$Release,
    [switch]$NoBuild,
    [switch]$Watch
)

$ErrorActionPreference = "Stop"
$config = if ($Release) { "Release" } else { "Debug" }
$projectPath = "$PSScriptRoot\QuoteBar"
$appPath = "$projectPath\bin\$config\net9.0-windows10.0.22621.0\win-x64\QuoteBar.exe"
$logPath = "$env:LOCALAPPDATA\QuoteBar\logs"

# Colors
function Write-Info { param($msg) Write-Host $msg -ForegroundColor Cyan }
function Write-Success { param($msg) Write-Host $msg -ForegroundColor Green }
function Write-Warn { param($msg) Write-Host $msg -ForegroundColor Yellow }
function Write-Err { param($msg) Write-Host $msg -ForegroundColor Red }
function Write-Line { Write-Host ("=" * 60) -ForegroundColor DarkGray }

# Kill existing instance
function Stop-QuoteBar {
    $existing = Get-Process -Name "QuoteBar" -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Warn "Stopping existing QuoteBar instance (PID: $($existing.Id))..."
        $existing | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }
}

# Build the app
function Build-QuoteBar {
    Write-Host ""
    Write-Info "[BUILD] Building QuoteBar ($config)..."
    $buildResult = dotnet build $projectPath -c $config 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Err "[BUILD FAILED]"
        $buildResult | ForEach-Object { Write-Host $_ }
        return $false
    }

    Write-Success "[BUILD] Success!"
    return $true
}

# Tail log file in background
function Start-LogTail {
    $today = Get-Date -Format "yyyy-MM-dd"
    $logFile = "$logPath\debug_$today.log"

    if (Test-Path $logFile) {
        Write-Host ""
        Write-Info "[LOGS] Tailing: $logFile"
        Write-Line

        # Get initial line count to only show new lines
        $initialLines = (Get-Content $logFile -ErrorAction SilentlyContinue | Measure-Object -Line).Lines

        # Start background job to tail logs
        $script:logJob = Start-Job -ScriptBlock {
            param($file, $skip)
            $lastLine = $skip
            while ($true) {
                if (Test-Path $file) {
                    $content = Get-Content $file -ErrorAction SilentlyContinue
                    $currentLines = $content.Count
                    if ($currentLines -gt $lastLine) {
                        $content[$lastLine..($currentLines-1)] | ForEach-Object {
                            if ($_ -match "ERROR|CRASHED|Exception") {
                                Write-Host $_ -ForegroundColor Red
                            }
                            elseif ($_ -match "WARN|Warning") {
                                Write-Host $_ -ForegroundColor Yellow
                            }
                            elseif ($_ -match "SUCCESS") {
                                Write-Host $_ -ForegroundColor Green
                            }
                            else {
                                Write-Host $_ -ForegroundColor Gray
                            }
                        }
                        $lastLine = $currentLines
                    }
                }
                Start-Sleep -Milliseconds 200
            }
        } -ArgumentList $logFile, $initialLines
    }
    else {
        Write-Warn "[LOGS] Log file not found yet: $logFile"
    }
}

# Main execution
Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "       QuoteBar Dev Server             " -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  Config: $config" -ForegroundColor DarkGray
Write-Host "  Press Ctrl+C to stop" -ForegroundColor DarkGray
Write-Host ""

try {
    # Stop any existing instance
    Stop-QuoteBar

    # Build unless -NoBuild flag
    if (-not $NoBuild) {
        $buildSuccess = Build-QuoteBar
        if (-not $buildSuccess) {
            exit 1
        }
    }

    # Verify app exists
    if (-not (Test-Path $appPath)) {
        Write-Err "[ERROR] App not found at: $appPath"
        Write-Warn "Try running without -NoBuild flag"
        exit 1
    }

    # Start log tailing
    Start-LogTail

    # Start the app and wait for it
    Write-Host ""
    Write-Success "[START] Launching QuoteBar..."
    Write-Line

    $process = Start-Process -FilePath $appPath -PassThru -NoNewWindow
    Write-Info "[PID] $($process.Id)"

    # Monitor the process and show logs
    while (-not $process.HasExited) {
        # Check for log job output
        if ($script:logJob) {
            Receive-Job -Job $script:logJob -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 100
    }

    # Process exited
    Write-Host ""
    Write-Line
    if ($process.ExitCode -eq 0) {
        Write-Success "[EXIT] QuoteBar closed normally (code: 0)"
    }
    else {
        Write-Err "[CRASH] QuoteBar exited with code: $($process.ExitCode)"

        # Show last few log lines on crash
        $today = Get-Date -Format "yyyy-MM-dd"
        $logFile = "$logPath\debug_$today.log"
        if (Test-Path $logFile) {
            Write-Host ""
            Write-Warn "[LAST LOG ENTRIES]"
            Get-Content $logFile -Tail 20 | ForEach-Object {
                if ($_ -match "ERROR|CRASHED|Exception") {
                    Write-Host $_ -ForegroundColor Red
                }
                else {
                    Write-Host $_ -ForegroundColor Gray
                }
            }
        }
    }
}
finally {
    # Cleanup
    if ($script:logJob) {
        Stop-Job -Job $script:logJob -ErrorAction SilentlyContinue
        Remove-Job -Job $script:logJob -ErrorAction SilentlyContinue
    }
    Write-Host ""
    Write-Host "[DONE] Dev server stopped." -ForegroundColor DarkGray
}

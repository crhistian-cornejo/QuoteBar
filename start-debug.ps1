# Start QuoteBar and capture any errors
$exePath = "$PSScriptRoot\NativeBar.WinUI\bin\Debug\net9.0-windows10.0.19041.0\win-x64\NativeBar.WinUI.exe"

Write-Host "Checking exe path: $exePath" -ForegroundColor Cyan
if (Test-Path $exePath) {
    Write-Host "Exe found. Starting..." -ForegroundColor Green
    
    # Start and wait for a bit
    $process = Start-Process -FilePath $exePath -PassThru
    Start-Sleep -Seconds 3
    
    # Check if process is still running
    if ($process.HasExited) {
        Write-Host "Process CRASHED! Exit code: $($process.ExitCode)" -ForegroundColor Red
    } else {
        Write-Host "Process running with PID: $($process.Id)" -ForegroundColor Green
    }
} else {
    Write-Host "Exe NOT found!" -ForegroundColor Red
}

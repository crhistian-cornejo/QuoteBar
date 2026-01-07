# Run QuoteBar and capture crash info
$exePath = "$PSScriptRoot\NativeBar.WinUI\bin\Debug\net9.0-windows10.0.19041.0\win-x64\NativeBar.WinUI.exe"

Write-Host "Starting QuoteBar..." -ForegroundColor Cyan

try {
    $pinfo = New-Object System.Diagnostics.ProcessStartInfo
    $pinfo.FileName = $exePath
    $pinfo.RedirectStandardError = $true
    $pinfo.RedirectStandardOutput = $true
    $pinfo.UseShellExecute = $false
    $pinfo.CreateNoWindow = $false
    
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $pinfo
    $process.Start() | Out-Null
    
    # Wait a bit
    Start-Sleep -Seconds 5
    
    if ($process.HasExited) {
        Write-Host "CRASHED! Exit code: $($process.ExitCode)" -ForegroundColor Red
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        if ($stdout) { Write-Host "STDOUT: $stdout" }
        if ($stderr) { Write-Host "STDERR: $stderr" -ForegroundColor Red }
    } else {
        Write-Host "Running with PID: $($process.Id)" -ForegroundColor Green
    }
} catch {
    Write-Host "Exception: $_" -ForegroundColor Red
}

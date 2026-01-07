# QuoteBar - Run Script
$appPath = "$PSScriptRoot\NativeBar.WinUI\bin\Debug\net9.0-windows10.0.19041.0\win-x64\NativeBar.WinUI.exe"

if (-not (Test-Path $appPath)) {
    Write-Host "App not found. Building..." -ForegroundColor Yellow
    dotnet build "$PSScriptRoot\NativeBar.WinUI" -c Debug
}

Write-Host "Starting QuoteBar..." -ForegroundColor Cyan
Start-Process -FilePath $appPath

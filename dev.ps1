<#
.SYNOPSIS
    Development script for NativeBar WinUI application

.DESCRIPTION
    Builds and runs the NativeBar application in development/debug mode.
    Supports hot-reload style development with automatic rebuild.

.PARAMETER Action
    The action to perform: build, run, watch, clean, release

.EXAMPLE
    .\dev.ps1 run
    .\dev.ps1 build
    .\dev.ps1 release
    .\dev.ps1 clean
#>

param(
    [Parameter(Position=0)]
    [ValidateSet("build", "run", "watch", "clean", "release", "publish")]
    [string]$Action = "run"
)

$ErrorActionPreference = "Stop"
$ProjectPath = Join-Path $PSScriptRoot "NativeBar.WinUI"
$SolutionPath = Join-Path $PSScriptRoot "NativeBar.sln"
$ExePath = Join-Path $ProjectPath "bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\NativeBar.WinUI.exe"
$DebugExePath = Join-Path $ProjectPath "bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\NativeBar.WinUI.exe"

function Write-Status($message) {
    Write-Host "[$([DateTime]::Now.ToString('HH:mm:ss'))] $message" -ForegroundColor Cyan
}

function Write-Success($message) {
    Write-Host "[$([DateTime]::Now.ToString('HH:mm:ss'))] $message" -ForegroundColor Green
}

function Write-Error($message) {
    Write-Host "[$([DateTime]::Now.ToString('HH:mm:ss'))] ERROR: $message" -ForegroundColor Red
}

function Stop-NativeBar {
    $process = Get-Process -Name "NativeBar.WinUI" -ErrorAction SilentlyContinue
    if ($process) {
        Write-Status "Stopping running NativeBar instance..."
        Stop-Process -Name "NativeBar.WinUI" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
}

function Build-Project {
    param([string]$Configuration = "Release")

    Write-Status "Building NativeBar ($Configuration)..."

    Push-Location $PSScriptRoot
    try {
        $result = dotnet build $SolutionPath -c $Configuration 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed!"
            $result | Write-Host
            return $false
        }
        Write-Success "Build completed successfully!"
        return $true
    }
    finally {
        Pop-Location
    }
}

function Run-App {
    param([string]$Configuration = "Release")

    $exe = if ($Configuration -eq "Debug") { $DebugExePath } else { $ExePath }

    if (-not (Test-Path $exe)) {
        Write-Error "Executable not found at: $exe"
        Write-Status "Building first..."
        if (-not (Build-Project -Configuration $Configuration)) {
            return
        }
    }

    Write-Status "Starting NativeBar..."
    Start-Process $exe
    Write-Success "NativeBar started!"
}

function Clean-Project {
    Write-Status "Cleaning build outputs..."

    Push-Location $PSScriptRoot
    try {
        dotnet clean $SolutionPath -c Debug 2>&1 | Out-Null
        dotnet clean $SolutionPath -c Release 2>&1 | Out-Null

        # Remove bin and obj folders
        Get-ChildItem -Path $ProjectPath -Include bin,obj -Recurse -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        Write-Success "Clean completed!"
    }
    finally {
        Pop-Location
    }
}

function Publish-App {
    Write-Status "Publishing NativeBar for distribution..."

    Push-Location $PSScriptRoot
    try {
        $publishPath = Join-Path $PSScriptRoot "publish"

        # Clean previous publish
        if (Test-Path $publishPath) {
            Remove-Item $publishPath -Recurse -Force
        }

        # Publish self-contained
        $result = dotnet publish $ProjectPath -c Release -r win-x64 --self-contained true -o $publishPath 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Publish failed!"
            $result | Write-Host
            return
        }

        Write-Success "Published to: $publishPath"
    }
    finally {
        Pop-Location
    }
}

# Main execution
switch ($Action) {
    "build" {
        Build-Project -Configuration "Release"
    }
    "run" {
        Stop-NativeBar
        if (Build-Project -Configuration "Release") {
            Run-App -Configuration "Release"
        }
    }
    "watch" {
        Write-Status "Starting watch mode (Ctrl+C to stop)..."
        Stop-NativeBar

        Push-Location $PSScriptRoot
        try {
            # Use dotnet watch for hot reload during development
            dotnet watch --project $ProjectPath run
        }
        finally {
            Pop-Location
        }
    }
    "clean" {
        Stop-NativeBar
        Clean-Project
    }
    "release" {
        Stop-NativeBar
        Build-Project -Configuration "Release"
    }
    "publish" {
        Stop-NativeBar
        Publish-App
    }
}

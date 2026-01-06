@echo off
REM Development script for NativeBar WinUI application
REM Usage: dev.cmd [action]
REM Actions: build, run, clean, release, publish

setlocal

set ACTION=%1
if "%ACTION%"=="" set ACTION=run

powershell -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %ACTION%

@echo off
setlocal

where pwsh >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required to build DotCraft Unity. 1>&2
    exit /b 1
)

pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scripts\build.ps1"
exit /b %ERRORLEVEL%

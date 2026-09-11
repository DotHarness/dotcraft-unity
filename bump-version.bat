@echo off
setlocal

where pwsh >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh^) is required to bump the DotCraft Unity version. 1>&2
    exit /b 1
)

set "VERSION=%~1"
if "%VERSION%"=="" (
    set /p VERSION=Enter new version ^(X.Y.Z^):
)

if "%VERSION%"=="" (
    echo Error: version is required. 1>&2
    exit /b 1
)

pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scripts\bump-version.ps1" "%VERSION%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Version bump failed.
    exit /b %EXIT_CODE%
)

echo.
echo Version bump succeeded: %VERSION%
echo Next check: git diff
exit /b 0

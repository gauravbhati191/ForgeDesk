@echo off
setlocal
title ForgeDesk Web Host

:: Move to the directory where this script is located (project root)
cd /d "%~dp0"

echo ===================================================
echo             Starting ForgeDesk Web Server
echo ===================================================
echo.
echo URL: http://localhost:5050
echo.
echo Launching your default web browser...
start "" "http://localhost:5050"

echo Running ForgeDesk.Web (.NET 8)...
echo Press Ctrl+C in this window to stop the web server.
echo.

dotnet run --project ForgeDesk.Web/ForgeDesk.Web.csproj --urls "http://localhost:5050"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] ForgeDesk.Web failed to start.
    echo Please ensure .NET 8 SDK is installed and on your PATH.
    echo.
    pause
)

@echo off
setlocal
cd /d "%~dp0"

fltmc >nul 2>&1
if errorlevel 1 (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set /p "AXON_VERSION="<"%~dp0VERSION"
title Axon v%AXON_VERSION% Offline Installer
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-Axon.ps1"
set "AXON_EXIT=%ERRORLEVEL%"
echo.
if not "%AXON_EXIT%"=="0" (
  echo Axon installation paused or failed. Read the message above.
  echo It is safe to restart Windows and run Install Axon again.
) else (
  echo Axon v%AXON_VERSION% installation completed successfully.
)
echo.
pause
exit /b %AXON_EXIT%

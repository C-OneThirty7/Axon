@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-AxonOperations.ps1"
if errorlevel 1 (
  echo.
  echo Axon Operations installation failed.
  pause
  exit /b 1
)
echo.
echo Axon Operations installation finished.
pause


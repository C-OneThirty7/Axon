@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Update-AxonControl.ps1"
if errorlevel 1 (
  echo.
  echo Axon Control update failed.
  pause
  exit /b 1
)
echo.
echo Axon Control update finished.
pause

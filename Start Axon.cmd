@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Axon-Operations.ps1" -Action Start -BundleRoot "%~dp0"
if errorlevel 1 (
  echo.
  echo Axon startup failed.
  pause
)

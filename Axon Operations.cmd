@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Axon-Operations.ps1" -Action Menu -BundleRoot "%~dp0"
if errorlevel 1 (
  echo.
  echo Axon Operations failed.
  pause
)

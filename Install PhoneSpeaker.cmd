@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AppPackages\PhoneSpeaker_1.0.0.0_x64_Test\Install.ps1"
if errorlevel 1 (
  echo.
  echo PhoneSpeaker installation did not complete. See the message above.
)
echo.
pause

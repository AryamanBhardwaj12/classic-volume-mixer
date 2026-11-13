@echo off
setlocal
powershell.exe -NoProfile -File "%~dp0Code\Install.ps1"
if errorlevel 1 (
  echo.
  echo Setup did not finish. Read the message above.
  echo You can also run dist\ClassicVolumeMixer.exe for portable use.
  pause
  exit /b 1
)
exit /b 0

@echo off
REM Double-click target for setting a till up. Wraps the PowerShell call so
REM nobody has to type -ExecutionPolicy Bypass, and pauses at the end so the
REM "Created/Removed/Unblocked" lines can actually be read.
REM
REM Arguments pass straight through:  Install-Kiosk.bat -Status
REM                                   Install-Kiosk.bat -Uninstall
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Kiosk.ps1" %*
echo.
pause

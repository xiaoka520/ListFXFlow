@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Manage-PtpTrial.ps1" -Action Install
pause

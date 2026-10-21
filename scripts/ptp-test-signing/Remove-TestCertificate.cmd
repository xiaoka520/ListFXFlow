@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Manage-TestCertificate.ps1" -Action Remove
pause

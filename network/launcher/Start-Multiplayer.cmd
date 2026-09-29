@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -STA -File "%~dp0launcher\Launcher.ps1"
if errorlevel 1 pause

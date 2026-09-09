@echo off
setlocal
cd /d "%~dp0"
title GoA2 v4 Server
echo Starting GoA2 v4 at http://127.0.0.1:3229
echo Keep this window open while playing.
python server.py
if errorlevel 1 (
  echo.
  echo GoA2 failed to start. Copy the error above for diagnosis.
  pause
)

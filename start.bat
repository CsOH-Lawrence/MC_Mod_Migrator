@echo off
cd /d "%~dp0"
where node >nul 2>nul || (
  echo Node.js 20+ is required. Install it from https://nodejs.org/
  pause
  exit /b 1
)
start "MC Mod Migrator" http://127.0.0.1:3728
node server.js
pause

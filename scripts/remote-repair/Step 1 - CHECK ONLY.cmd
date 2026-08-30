@echo off
title Sati database check - makes no changes
setlocal
set "SCRIPT=%~dp0Apply-ProductionHistoryReconciliation.ps1"

if not exist "%SCRIPT%" goto missing

echo.
echo Checking the Sati database. This makes NO changes to it.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-database-check.txt'; Unblock-File -LiteralPath '%SCRIPT%' -ErrorAction SilentlyContinue; & '%SCRIPT%' -WhatIfOnly *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' Send that file to Josh BEFORE running Step 2.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:missing
echo.
echo Could not find Apply-ProductionHistoryReconciliation.ps1 next to this file.
echo Copy all three files into the same folder and try again.
echo.
pause
exit /b 1

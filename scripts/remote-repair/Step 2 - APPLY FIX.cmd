@echo off
title Sati database fix - writes one record
setlocal
set "SCRIPT=%~dp0Apply-ProductionHistoryReconciliation.ps1"

if not exist "%SCRIPT%" goto missing

echo.
echo  Run "Step 1 - CHECK ONLY" first and send Josh the result.
echo  Only continue if Josh has told you to.
echo.
echo  This writes ONE record to the Sati database. It does not
echo  change any of your consumer records.
echo.
set /p CONFIRM="Type YES and press Enter to continue: "
if /I not "%CONFIRM%"=="YES" goto cancelled

echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); if (-not $d -or -not (Test-Path $d)) { $d = $env:USERPROFILE }; $log = Join-Path $d 'Sati-database-fix.txt'; Unblock-File -LiteralPath '%SCRIPT%' -ErrorAction SilentlyContinue; & '%SCRIPT%' *>&1 | Tee-Object -FilePath $log; Write-Host ''; Write-Host '============================================================'; Write-Host (' Saved to: ' + $log); Write-Host ''; Write-Host ' Now start Sati. It will back up your database and finish'; Write-Host ' updating it. If it still refuses to start, send Josh both'; Write-Host ' the message it shows and the file above.'; Write-Host '============================================================'"

echo.
pause
exit /b 0

:cancelled
echo.
echo Cancelled. Nothing was changed.
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

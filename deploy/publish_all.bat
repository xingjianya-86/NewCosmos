@echo off
rem ============================================================================
rem  publish_all.bat  -  one-click dual-end release (Windows + Android AOT)
rem  build -> sign -> upload -> round-trip download verify -> GitHub
rem
rem  Usage (version read from NewCosmos.csproj unless passed):
rem    publish_all.bat
rem    publish_all.bat -Version 1.1.20261010
rem    publish_all.bat -Version 1.1.20261010 -WithPatch
rem    publish_all.bat -Version 1.1.20261010 -SoftUpdate
rem    publish_all.bat -DryRun            (plan only: no build/upload)
rem    publish_all.bat -NonInteractive -SkipGit
rem
rem  Full console output is also written to publish_all.last.log; the window
rem  stays open at the end (pause) so errors are visible.
rem  Set env NEWCOSMOS_DB_PASSWORD for release history / prev-version lookup.
rem  Internal host comes from deploy\deploy.local.ps1 (gitignored).
rem ============================================================================
setlocal
chcp 65001 >nul
set "PS1=%~dp0publish_all.ps1"
set "LOG=%~dp0publish_all.last.log"
echo ================================================================
echo  NewCosmos publish_all
echo  log: %LOG%
echo ================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "$log='%LOG%'; try { Start-Transcript -Path $log -Force | Out-Null } catch {}; $ok=$true; try { & '%PS1%' %* } catch { $ok=$false; Write-Host ''; Write-Host ('ERROR: ' + $_) -ForegroundColor Red }; try { Stop-Transcript | Out-Null } catch {}; if ($ok) { exit 0 } else { exit 1 }"
set "RC=%ERRORLEVEL%"
echo.
echo ================================================================
echo  ExitCode = %RC%
echo  Log      = %LOG%
echo ================================================================
pause
exit /b %RC%

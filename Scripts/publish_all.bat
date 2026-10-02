@echo off
rem ============================================================================
rem  publish_all.bat  -  one-click dual-end release launcher
rem
rem  Windows installer + Android (AOT) APK -> sign -> upload -> optional GitHub.
rem  Version is read from NewCosmos.csproj unless passed.
rem
rem  Usage:
rem    publish_all.bat
rem    publish_all.bat -Version 1.1.20261008
rem    publish_all.bat -SkipGit
rem
rem  Set env NEWCOSMOS_DB_PASSWORD to record release history; internal host comes
rem  from Scripts\deploy.local.ps1 (gitignored).
rem ============================================================================
setlocal
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_all.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_all.ps1" %*
)
exit /b %ERRORLEVEL%

@echo off
rem ============================================================================
rem  publish_all.bat  -  one-click dual-end release (Windows + Android AOT)
rem  build -> sign -> upload -> round-trip download verify -> GitHub
rem
rem  Usage (version read from NewCosmos.csproj unless passed):
rem    publish_all.bat
rem    publish_all.bat -Version 1.1.20261010
rem    publish_all.bat -Version 1.1.20261010 -WithPatch     (incremental patch)
rem    publish_all.bat -Version 1.1.20261010 -SoftUpdate    (non-forced update)
rem    publish_all.bat -MinSupported 1.1.20261004           (explicit floor)
rem    publish_all.bat -SkipDownloadVerify                  (skip big download)
rem    publish_all.bat -SkipGit
rem
rem  Set env NEWCOSMOS_DB_PASSWORD for release history / prev-version lookup.
rem  Internal host comes from deploy\deploy.local.ps1 (gitignored).
rem ============================================================================
setlocal
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_all.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_all.ps1" %*
)
exit /b %ERRORLEVEL%

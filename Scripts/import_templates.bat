@echo off
rem ============================================================================
rem  import_templates.bat  -  launcher for import_templates.ps1
rem
rem  Usage:
rem    import_templates.bat
rem        -> uses default manifest: Scripts\templates_manifest.json
rem    import_templates.bat -SourceDir ..\Templates_NEW
rem    import_templates.bat -Manifest templates_manifest.json -DryRun
rem
rem  DB password is read from env var NEWCOSMOS_DB_PASSWORD;
rem  if unset, the script prompts interactively.
rem ============================================================================
setlocal
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0import_templates.ps1" -Manifest "%~dp0templates_manifest.json"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0import_templates.ps1" %*
)
exit /b %ERRORLEVEL%

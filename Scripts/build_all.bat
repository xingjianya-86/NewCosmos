@echo off
rem ============================================================================
rem  build_all.bat  -  launcher for build_all.ps1
rem  One-click local build: Windows installer + Android APK + incremental patch
rem  + runnable Debug build. Artifacts are placed under publish\ (no upload).
rem
rem  Usage:
rem    build_all.bat
rem    build_all.bat -Version 1.1.20261005
rem    build_all.bat -SkipAndroid -SkipPatch
rem    build_all.bat -DryRun
rem ============================================================================
setlocal
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_all.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_all.ps1" %*
)
exit /b %ERRORLEVEL%

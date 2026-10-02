@echo off
rem ============================================================================
rem  download_update.bat  -  download the latest package from the update server
rem  (fetch manifest -> verify signature -> download -> verify SHA256)
rem
rem  Usage:
rem    download_update.bat
rem    download_update.bat -Platform android
rem    download_update.bat -Platform windows -OutputDir D:\updates
rem ============================================================================
setlocal
if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0download_update.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0download_update.ps1" %*
)
exit /b %ERRORLEVEL%

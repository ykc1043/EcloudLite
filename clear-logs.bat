@echo off
setlocal

set "LITE_LOG_DIR=%~dp0data\logs"
set "RUNTIME_LOG_DIR=%~dp0cmss-runtime\log"

tasklist /FI "IMAGENAME eq EcloudLite.exe" 2>nul | find /I "EcloudLite.exe" >nul
if not errorlevel 1 (
    echo Please close EcloudLite.exe before clearing logs.
    pause
    exit /b 1
)

call :clear_log_dir "%LITE_LOG_DIR%"
call :clear_log_dir "%RUNTIME_LOG_DIR%"

echo.
echo Settings and cmss-runtime binaries were not changed.
pause
exit /b 0

:clear_log_dir
if not exist "%~1" (
    echo Log directory not found, skipped: %~1
    exit /b 0
)

del /f /q "%~1\*" >nul 2>&1
for /d %%D in ("%~1\*") do if exist "%%~fD" rd /s /q "%%~fD" >nul 2>&1
echo Log files cleared: %~1
exit /b 0

@echo off
echo ========================================================
echo Uninstalling old 1C Authorizator
echo ========================================================
echo.

:: Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ========================================================
    echo ERROR: Please run this script as Administrator!
    echo Right-click uninstall.bat and select "Run as administrator".
    echo ========================================================
    pause
    exit /b
)

echo [1/4] Stopping processes...
taskkill /f /im 1c-auth.exe >nul 2>&1
taskkill /f /im 1c-auth-debug.exe >nul 2>&1
powershell -Command "Get-CimInstance Win32_Process -Filter \"Name = 'powershell.exe' AND CommandLine LIKE '%%monitor.ps1%%'\" | Invoke-CimMethod -MethodName Terminate" >nul 2>&1

echo [2/4] Removing Scheduled Task...
schtasks /delete /tn "1C Authorizator" /f >nul 2>&1

echo [3/4] Removing Monitor Autostart...
set "STARTUP_FOLDER=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
if exist "%STARTUP_FOLDER%\1C_Auth_Monitor.lnk" (
    del /f /q "%STARTUP_FOLDER%\1C_Auth_Monitor.lnk" >nul 2>&1
)

echo [4/4] Removing old application folder...
set "TARGET_DIR=C:\Across\1C Authorizator"
if exist "%TARGET_DIR%" (
    rmdir /s /q "%TARGET_DIR%" >nul 2>&1
)

echo.
echo ========================================================
echo DONE! Old 1C Authorizator has been completely removed.
echo (Registry settings were preserved as requested)
echo ========================================================
pause

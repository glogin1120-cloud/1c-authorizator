@echo off
echo ========================================================
echo Installing 1C Authorizator (User Level)
echo ========================================================
echo.

set "TARGET_DIR=%LOCALAPPDATA%\1C_Authorizator"

echo [1/4] Stopping running processes to release file locks...
taskkill /f /im 1c-auth.exe >nul 2>&1
taskkill /f /im 1c-auth-debug.exe >nul 2>&1

echo [2/4] Creating folder %TARGET_DIR%...
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

echo [3/4] Copying files...
xcopy /Y /E "%~dp0*" "%TARGET_DIR%\" >nul
if exist "%TARGET_DIR%\install.bat" del /q "%TARGET_DIR%\install.bat"
if exist "%TARGET_DIR%\instructions.txt" del /q "%TARGET_DIR%\instructions.txt"
if exist "%TARGET_DIR%\1c_auth_task.xml" del /q "%TARGET_DIR%\1c_auth_task.xml"

:: Clean up old monitor startup links if exist
del /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\1C_Auth_Monitor.lnk" >nul 2>&1
del /q "%TARGET_DIR%\*.txt" >nul 2>&1

echo [4/4] Setting up Startup folder shortcut...
:: Clean up old scheduler tasks (fails silently if not admin, which is fine)
schtasks /delete /tn "1C Authorizator" /f >nul 2>&1

:: Create a shortcut in the User's Startup directory using PowerShell
set "SHORTCUT_PATH=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\1C_Authorizator.lnk"
powershell -NoProfile -Command "$WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%SHORTCUT_PATH%'); $Shortcut.TargetPath = '%TARGET_DIR%\1c-auth.exe'; $Shortcut.WorkingDirectory = '%TARGET_DIR%'; $Shortcut.Save()"

echo.
echo ========================================================
echo DONE! 
echo Installed to: %TARGET_DIR%
echo Startup shortcut created in: %SHORTCUT_PATH%
echo (Application will automatically start on Windows login)
echo.
echo To start the application now, open:
echo %TARGET_DIR%\1c-auth.exe
echo ========================================================
pause

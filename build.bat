@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo Building 1C Authorizator
echo ========================================================
echo.

set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC_PATH%" (
    echo Error: C# Compiler not found at %CSC_PATH%
    echo Make sure .NET Framework 4.8 is installed.
    exit /b 1
)

set "SOURCE_FILES=1c-auth.cs Config.cs"
set "OUT_FILE=1c-auth.exe"
set "WIN32_ICON=across.ico"

echo [1/2] Compiling...
if exist "%WIN32_ICON%" (
    "%CSC_PATH%" /nologo /target:winexe /out:"%OUT_FILE%" /win32icon:"%WIN32_ICON%" /reference:System.dll,System.Windows.Forms.dll,System.Drawing.dll %SOURCE_FILES%
) else (
    "%CSC_PATH%" /nologo /target:winexe /out:"%OUT_FILE%" /reference:System.dll,System.Windows.Forms.dll,System.Drawing.dll %SOURCE_FILES%
)

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build FAILED!
    exit /b %ERRORLEVEL%
)

echo [2/2] Build successful: %OUT_FILE%
echo.
echo ========================================================
echo DONE!
echo ========================================================
pause

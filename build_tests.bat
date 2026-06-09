@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo Building and Running Tests
echo ========================================================
echo.

set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC_PATH%" (
    echo Error: C# Compiler not found at %CSC_PATH%
    exit /b 1
)

set "SOURCE_FILES=1c-auth.cs Config.cs 1C_Authorizator_CS_TEST\Tests.cs"
set "OUT_FILE=1C_Authorizator_CS_TEST\RunTests.exe"

echo [1/2] Compiling Tests...
"%CSC_PATH%" /nologo /target:exe /main:Tests /out:"%OUT_FILE%" /reference:System.dll,System.Windows.Forms.dll,System.Drawing.dll %SOURCE_FILES%

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Test Build FAILED!
    exit /b %ERRORLEVEL%
)

echo [2/2] Running Tests...
echo.
"%OUT_FILE%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Tests FAILED!
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo ALL TESTS PASSED!
echo ========================================================
pause

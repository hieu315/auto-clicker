@echo off
setlocal
cd /d "%~dp0"

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    echo [ERROR] Khong tim thay trinh bien dich C# tai %CSC%
    exit /b 1
)

if not exist "macros" mkdir "macros"

taskkill /f /im SwiftMacroPro.exe >nul 2>&1
taskkill /f /im AutoClicker.exe >nul 2>&1

echo Dang bien dich SwiftMacroPro.exe (.NET Framework 4.8 + PerMonitorV2 Manifest)...
%CSC% /nologo /target:winexe /out:SwiftMacroPro.exe /win32manifest:app.manifest ^
    /r:System.dll ^
    /r:System.Core.dll ^
    /r:System.Drawing.dll ^
    /r:System.Windows.Forms.dll ^
    /r:System.Web.Extensions.dll ^
    src\NativeMethods.cs ^
    src\Models.cs ^
    src\MacroEngine.cs ^
    src\MainForm.cs

if %ERRORLEVEL% neq 0 (
    echo [FAILED] Bien dich that bai!
    exit /b %ERRORLEVEL%
)

copy /y SwiftMacroPro.exe AutoClicker.exe >nul 2>&1
echo [SUCCESS] Da tao thanh cong: %~dp0SwiftMacroPro.exe
exit /b 0

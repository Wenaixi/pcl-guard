@echo off
setlocal
cd /d "%~dp0"

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. Please ensure .NET Framework 4.5+ is installed.
    exit /b 1
)

if not exist "bin" mkdir "bin"

echo [BUILD] Compiling pcl-guard.exe with embedded PCL baseline...
"%CSC%" /target:winexe /optimize+ /out:bin\pcl-guard.exe /win32icon:src\PCL_Icon.ico /resource:"src\Plain Craft Launcher.exe",PCL_Baseline.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll src\Program.cs

if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] Build output: bin\pcl-guard.exe
) else (
    echo [FAILED] Compilation failed with exit code: %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

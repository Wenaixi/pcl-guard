@echo off
setlocal
cd /d "%~dp0"

set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    set CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe
)

if not exist "%CSC%" (
    echo [ERROR] 未找到 C# 编译器 csc.exe，请确认已安装 .NET Framework 4.5 及以上版本。
    exit /b 1
)

if not exist "bin" mkdir "bin"

echo 正在编译 pcl-guard.exe...
"%CSC%" /target:winexe /optimize+ /out:bin\pcl-guard.exe /win32icon:src\PCL_Icon.ico /resource:"src\Plain Craft Launcher.exe",PCL_Baseline.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll src\Program.cs

if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] 编译完成: bin\pcl-guard.exe
) else (
    echo [FAILED] 编译失败，退出码: %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

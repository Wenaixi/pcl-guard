$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $scriptDir

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    Write-Error "csc.exe not found. Please install .NET Framework 4.5+."
}

if (-not (Test-Path "bin")) { New-Item -ItemType Directory -Path "bin" | Out-Null }

Write-Host "[BUILD] Compiling pcl-guard.exe..." -ForegroundColor Cyan
$argsList = @(
    "/target:winexe",
    "/optimize+",
    "/out:bin\pcl-guard.exe",
    "/win32icon:src\PCL_Icon.ico",
    "/resource:src\Plain Craft Launcher.exe,PCL_Baseline.exe",
    "/r:System.dll,System.Windows.Forms.dll,System.Drawing.dll",
    "src\Program.cs"
)

& $csc $argsList

if (Test-Path "bin\pcl-guard.exe") {
    $item = Get-Item "bin\pcl-guard.exe"
    Write-Host "[SUCCESS] Successfully built: $($item.FullName) ($($item.Length) bytes)" -ForegroundColor Green
} else {
    Write-Error "Build failed!"
}

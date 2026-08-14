$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]$identity
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

$installDir = Join-Path $env:ProgramData "WebCamKu\VirtualCamera"
$manager = Join-Path $installDir "WebCamKu.VirtualCamera.Manager.exe"
if (Test-Path $manager) { & $manager shutdown | Out-Null; & $manager remove; if ($LASTEXITCODE -ne 0) { throw "Virtual camera removal failed." } }
Stop-Service -Name FrameServerMonitor -Force -ErrorAction SilentlyContinue
Stop-Service -Name FrameServer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Remove-Item "Registry::HKEY_LOCAL_MACHINE\Software\Classes\CLSID\{462EBC79-4F6A-438D-8298-1F095BCF7A41}" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\{462EBC79-4F6A-438D-8298-1F095BCF7A41}" -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $installDir) { Remove-Item -LiteralPath $installDir -Recurse -Force }
Start-Service -Name FrameServerMonitor -ErrorAction SilentlyContinue
Write-Host "WebCamKu Camera removed."

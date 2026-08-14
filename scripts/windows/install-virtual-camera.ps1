param([ValidateSet("Debug", "Release")][string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]$identity
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window. COM registration for Windows Frame Server requires administrator access."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$sourceDll = Join-Path $repoRoot "windows\WebCamKu.VirtualCamera\x64\$Configuration\WebCamKu.VirtualCamera.dll"
$sourceManager = Join-Path $repoRoot "windows\WebCamKu.VirtualCamera.Manager\x64\$Configuration\WebCamKu.VirtualCamera.Manager.exe"
if (-not (Test-Path $sourceDll) -or -not (Test-Path $sourceManager)) { throw "Build Windows $Configuration first." }

$installDir = Join-Path $env:ProgramData "WebCamKu\VirtualCamera"
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$installedDll = Join-Path $installDir "WebCamKu.VirtualCamera.dll"
$installedManager = Join-Path $installDir "WebCamKu.VirtualCamera.Manager.exe"
if (Test-Path $installedManager) { & $installedManager shutdown | Out-Null }
Stop-Service -Name FrameServerMonitor -Force -ErrorAction SilentlyContinue
Stop-Service -Name FrameServer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Copy-Item -LiteralPath $sourceDll -Destination $installedDll -Force
Copy-Item -LiteralPath $sourceManager -Destination $installedManager -Force

$classKey = "Registry::HKEY_LOCAL_MACHINE\Software\Classes\CLSID\{462EBC79-4F6A-438D-8298-1F095BCF7A41}\InProcServer32"
New-Item -Path $classKey -Force | Out-Null
Set-Item -Path $classKey -Value $installedDll
New-ItemProperty -Path $classKey -Name ThreadingModel -Value Both -PropertyType String -Force | Out-Null
Remove-Item "Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\{462EBC79-4F6A-438D-8298-1F095BCF7A41}" -Recurse -Force -ErrorAction SilentlyContinue

Start-Service -Name FrameServerMonitor -ErrorAction SilentlyContinue

& $installedManager start
if ($LASTEXITCODE -ne 0) { throw "MFCreateVirtualCamera registration failed with exit code $LASTEXITCODE." }
Write-Host "WebCamKu Camera installed and started."

param(
    [ValidateSet("Standard", "Machine")][string]$Scope = "Standard",
    [string]$ObsRoot = "C:\Program Files\obs-studio"
)
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$source = Join-Path $repoRoot "obs-plugin\x64\Release\webcamku-obs.dll"
if (-not (Test-Path $source)) { throw "Build obs-plugin\WebCamKuObsPlugin.vcxproj Release|x64 first." }
$destination = if ($Scope -eq "Machine") {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]$identity
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Machine scope writes to Program Files; run PowerShell as Administrator."
    }
    Join-Path $ObsRoot "obs-plugins\64bit\webcamku-obs.dll"
} else {
    Join-Path $env:ProgramData "obs-studio\plugins\webcamku-obs\bin\64bit\webcamku-obs.dll"
}
Get-Process obs64 -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
Write-Host "Installed WebCamKu Source plugin to $destination"

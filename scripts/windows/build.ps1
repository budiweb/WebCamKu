param([ValidateSet("Debug", "Release")][string]$Configuration = "Debug")

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "Visual Studio Build Tools 2022 with the C++ workload is required." }
$installation = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw "Visual Studio C++ build tools were not found." }
$msbuild = Join-Path $installation "MSBuild\Current\Bin\MSBuild.exe"
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

& $msbuild (Join-Path $repoRoot "windows\WebCamKu.Video\WebCamKu.Video.vcxproj") /m /p:Configuration=$Configuration /p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $msbuild (Join-Path $repoRoot "windows\WebCamKu.VirtualCamera\WebCamKu.VirtualCamera.vcxproj") /m /p:Configuration=$Configuration /p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $msbuild (Join-Path $repoRoot "windows\WebCamKu.VirtualCamera.Manager\WebCamKu.VirtualCamera.Manager.vcxproj") /m /p:Configuration=$Configuration /p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Configuration -eq "Release") {
    & $msbuild (Join-Path $repoRoot "obs-plugin\WebCamKuObsPlugin.vcxproj") /m /p:Configuration=Release /p:Platform=x64
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
& $dotnet restore (Join-Path $repoRoot "windows\WebCamKu.sln")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build (Join-Path $repoRoot "windows\WebCamKu.sln") --no-restore --configuration $Configuration
exit $LASTEXITCODE

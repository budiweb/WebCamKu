$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$header = Join-Path $repoRoot ".tools\obs-studio\libobs\obs-module.h"
if (-not (Test-Path $header)) {
    throw "Matching OBS source headers are missing at .tools\obs-studio. Checkout the installed OBS tag first."
}
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$installation = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw "Visual Studio C++ build tools were not found." }
$msbuild = Join-Path $installation "MSBuild\Current\Bin\MSBuild.exe"
& $msbuild (Join-Path $repoRoot "obs-plugin\WebCamKuObsPlugin.vcxproj") /m /p:Configuration=Release /p:Platform=x64
exit $LASTEXITCODE

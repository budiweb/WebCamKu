param(
    [Parameter(Mandatory = $true)]
    [string]$PhoneIp,
    [int]$DurationSeconds = 1800,
    [string]$AdbSerial = "RR8R407VQ1V"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$adb = Join-Path $repoRoot ".tools\android-sdk\platform-tools\adb.exe"
$results = Join-Path $repoRoot ".tools\m0.4-results"
New-Item -ItemType Directory -Force -Path $results | Out-Null
$env:WEBCAMKU_TEST_HOST = $PhoneIp
$env:WEBCAMKU_TEST_DURATION_SECONDS = $DurationSeconds.ToString()

$test = Start-Process -FilePath $dotnet -ArgumentList @(
    "test", "WebCamKu.Core.Tests\WebCamKu.Core.Tests.csproj",
    "--filter", "Category=PhysicalDevice",
    "--logger", "console;verbosity=normal"
) -WorkingDirectory (Join-Path $repoRoot "windows") -NoNewWindow -PassThru `
    -RedirectStandardOutput (Join-Path $results "test.out.log") `
    -RedirectStandardError (Join-Path $results "test.err.log")

$samples = @()
while (-not $test.HasExited) {
    $test.Refresh()
    $androidMem = & $adb -s $AdbSerial shell dumpsys meminfo id.webcamku.app 2>$null |
        Select-String "TOTAL PSS:" | Select-Object -First 1
    $testHost = Get-Process testhost -ErrorAction SilentlyContinue | Select-Object -First 1
    $samples += [pscustomobject]@{
        Timestamp = (Get-Date).ToString("o")
        AndroidTotalPssLine = if ($androidMem) { $androidMem.Line.Trim() } else { "unavailable" }
        WindowsWorkingSetBytes = if ($testHost) { $testHost.WorkingSet64 } else { 0 }
    }
    $samples[-1] | ConvertTo-Json -Compress
    Start-Sleep -Seconds 30
}
$test.WaitForExit()
$samples | ConvertTo-Json | Set-Content (Join-Path $results "memory-samples.json")
Get-Content (Join-Path $results "test.out.log")
if ($test.ExitCode -ne 0) { exit $test.ExitCode }

param(
    [string]$DotnetExe = "dotnet",
    [ValidateSet('Auto','Dotnet','Unity')][string]$Runner = 'Auto',
    [string]$UnityExe
)
$ErrorActionPreference = "Stop"
$goaRoot = Split-Path -Parent $PSScriptRoot
if ($Runner -eq 'Unity') { & "$PSScriptRoot/test-unity.ps1" -UnityExe $UnityExe; return }
if (-not (Get-Command $DotnetExe -ErrorAction SilentlyContinue)) {
    $goaLocalSdk = Join-Path $env:LOCALAPPDATA "Goa2V1Toolchain\dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $goaLocalSdk) { $DotnetExe = $goaLocalSdk }
    else { throw "Install the SDK specified in global.json, or provide -DotnetExe." }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
Push-Location $goaRoot
try {
    & $DotnetExe restore tests/Goa2.Core.Tests/Goa2.Core.Tests.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Dependency restore failed." }
    $goaStarted = [DateTime]::UtcNow
    & $DotnetExe test tests/Goa2.Core.Tests/Goa2.Core.Tests.csproj --no-restore --configuration Release --logger "trx;LogFileName=core.trx" --results-directory artifacts/tests
    if ($LASTEXITCODE -ne 0) {
        $goaAppControlOnly = $false
        $goaTrxPath = Join-Path $goaRoot 'artifacts/tests/core.trx'
        if ($Runner -eq 'Auto' -and (Test-Path -LiteralPath $goaTrxPath) -and (Get-Item -LiteralPath $goaTrxPath).LastWriteTimeUtc -ge $goaStarted) {
            [xml]$goaTrx = Get-Content -LiteralPath $goaTrxPath -Raw
            $goaFailures = @($goaTrx.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Failed')
            $goaAppControlOnly = $goaFailures.Count -gt 0 -and @($goaFailures | Where-Object { $_.Output.ErrorInfo.Message -notmatch '0x800711C7' }).Count -eq 0
        }
        if (-not $goaAppControlOnly) { throw 'Core tests failed. Inspect artifacts/tests/core.trx.' }
        Write-Warning 'Windows application control blocked the .NET test host. Running the identical test sources with Unity EditMode; the .NET environment failure remains in core.trx.'
        & "$PSScriptRoot/test-unity.ps1" -UnityExe $UnityExe
    }
} finally { Pop-Location }

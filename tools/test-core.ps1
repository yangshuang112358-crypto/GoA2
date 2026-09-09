param([string]$DotnetExe = "dotnet")
$ErrorActionPreference = "Stop"
$goaRoot = Split-Path -Parent $PSScriptRoot
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
    & $DotnetExe test tests/Goa2.Core.Tests/Goa2.Core.Tests.csproj --no-restore --configuration Release --logger "trx;LogFileName=core.trx" --results-directory artifacts/tests
    if ($LASTEXITCODE -ne 0) { throw "Core tests failed." }
} finally { Pop-Location }

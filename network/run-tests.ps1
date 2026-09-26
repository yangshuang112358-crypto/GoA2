param([string]$DotnetExe, [string]$PythonExe, [switch]$IncludeCore)
$ErrorActionPreference = 'Stop'
$goaNetworkRoot = Split-Path -Parent $PSScriptRoot
if (-not $DotnetExe) { $DotnetExe = Join-Path $env:LOCALAPPDATA 'Goa2V1Toolchain/dotnet/dotnet.exe' }
if (-not $PythonExe) { $PythonExe = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' }
$goaRun = Join-Path $goaNetworkRoot ('artifacts/network/suite-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $goaRun | Out-Null
Push-Location $goaNetworkRoot
try {
    # The engine-66 baseline has two CS8602 warnings. Keep them visible in the build log.
    & $DotnetExe build network/tests/Goa2.Network.TestHost/Goa2.Network.TestHost.csproj -c Release -p:RestoreLockedMode=true -p:WarningsNotAsErrors=CS8602 *> (Join-Path $goaRun 'server-build.log')
    if ($LASTEXITCODE -ne 0) { throw "Server build failed: $goaRun" }
    & $DotnetExe build network/tests/Goa2.Network.ClientHarness/Goa2.Network.ClientHarness.csproj -c Release -p:RestoreLockedMode=true *> (Join-Path $goaRun 'client-build.log')
    if ($LASTEXITCODE -ne 0) { throw "Client build failed: $goaRun" }
    & $PythonExe network/tests/test_client.py *> (Join-Path $goaRun 'client-unit.log')
    if ($LASTEXITCODE -ne 0) { throw "Client unit tests failed: $goaRun" }
    foreach ($goaMode in @('', '--pending', '--csharp')) {
        $goaArguments = @('network/tests/acceptance.py')
        if ($goaMode) { $goaArguments += $goaMode }
        & $PythonExe @goaArguments | Tee-Object -FilePath (Join-Path $goaRun ('runs' + $goaMode + '.txt'))
        if ($LASTEXITCODE -ne 0) { throw "Network suite failed: $goaMode; $goaRun" }
    }
    if ($IncludeCore) {
        # Existing broad test source set has several nullable warnings. Do not edit it here.
        & $DotnetExe test tests/Goa2.Core.Tests/Goa2.Core.Tests.csproj -c Release -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~SessionTests' --logger 'trx;LogFileName=core-session.trx' --results-directory $goaRun *> (Join-Path $goaRun 'core-session.log')
        if ($LASTEXITCODE -ne 0) { throw "Core session tests failed: $goaRun" }
    }
    Write-Output "PASS: $goaRun (network processes, not Unity window acceptance)"
} finally { Pop-Location }

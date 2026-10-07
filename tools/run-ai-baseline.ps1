param(
    [int]$Seed=101,
    [ValidateRange(1,16)][int]$Pairs=1,
    [ValidateRange(1,9000)][int]$Steps=900,
    [ValidateSet('simple-random','random-random','simple-simple')][string]$Matchup='simple-random',
    [string]$Output,
    [switch]$Test
)
$ErrorActionPreference='Stop'
$aiRoot=Split-Path -Parent $PSScriptRoot
$aiDotnet=Join-Path $env:LOCALAPPDATA 'Goa2V1Toolchain/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $aiDotnet)) { $aiDotnet='dotnet' }
if (-not $Output) { $Output=Join-Path $aiRoot ('artifacts/ai/'+[Guid]::NewGuid().ToString('N')) }
Push-Location $aiRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    if ($Test) {
        & $aiDotnet test ai/Goa2.Ai.Tests -c Release -p:IncludeSourceRevisionInInformationalVersion=false -p:WarningsNotAsErrors=CS8602 -p:RestoreLockedMode=true --logger 'trx' --results-directory artifacts/ai/tests
        if ($LASTEXITCODE -ne 0) { throw 'AI tests failed.' }
    }
    & $aiDotnet build ai/Goa2.Ai.Cli -c Release -p:IncludeSourceRevisionInInformationalVersion=false -p:WarningsNotAsErrors=CS8602 -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw 'AI build failed.' }
    & $aiDotnet ai/Goa2.Ai.Cli/bin/Release/net10.0/Goa2.Ai.Cli.dll $aiRoot $Output $Seed $Pairs $Steps $Matchup
    if ($LASTEXITCODE -ne 0) { throw "AI run failed; preserve and inspect $Output" }
    Write-Output "AI evidence: $Output"
} finally { Pop-Location }

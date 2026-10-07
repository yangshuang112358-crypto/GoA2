param(
    [string]$Output,
    [ValidateRange(1,30)][int]$MaxMinutes=15,
    [ValidateRange(1,500)][int]$Updates=300
)
$ErrorActionPreference='Stop'
$aiRoot=Split-Path -Parent $PSScriptRoot
$aiDotnet=Join-Path $env:LOCALAPPDATA 'Goa2V1Toolchain/dotnet/dotnet.exe'
$aiPython=Join-Path $aiRoot '.venv-ai/Scripts/python.exe'
if(-not(Test-Path -LiteralPath $aiPython)){throw 'Prepare isolated .venv-ai first.'}
if(-not $Output){$Output=Join-Path $aiRoot ('artifacts/ai-training/defense-'+[Guid]::NewGuid().ToString('N'))}
Push-Location $aiRoot
try {
    & $aiDotnet build ai/Goa2.Ai.Cli -c Release -t:Rebuild -p:EnableSourceLink=false -p:EmbedUntrackedSources=false -p:IncludeSourceRevisionInInformationalVersion=false -p:WarningsNotAsErrors=CS8602 -p:RestoreLockedMode=true
    if($LASTEXITCODE -ne 0){throw 'AI build failed'}
    $aiDefenseData=$Output+'-defense-data'
    $aiVisitedData=$Output+'-visited-data'
    & $aiDotnet ai/Goa2.Ai.Cli/bin/Release/net10.0/Goa2.Ai.Cli.dll curriculum $aiRoot ai/defense-sources.json $aiDefenseData
    if($LASTEXITCODE -ne 0){throw "Defense export failed; preserve $aiDefenseData"}
    & $aiDotnet ai/Goa2.Ai.Cli/bin/Release/net10.0/Goa2.Ai.Cli.dll curriculum $aiRoot ai/model-visited-sources.json $aiVisitedData
    if($LASTEXITCODE -ne 0){throw "Model-visited export failed; preserve $aiVisitedData"}
    & $aiDotnet build-server shutdown
    & $aiPython ai/trainer/defense.py --data docs/verification/ai-stage3-20261007/teaching-v3-01 $aiDefenseData $aiVisitedData --parent docs/verification/ai-stage3-20261007/curriculum-v3-01/warmup.pt --output $Output --updates $Updates --max-minutes $MaxMinutes
    if($LASTEXITCODE -ne 0){throw "Bounded defense experiment failed; preserve $Output"}
} finally {Pop-Location}

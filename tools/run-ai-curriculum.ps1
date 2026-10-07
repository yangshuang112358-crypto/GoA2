param(
    [string]$Data,
    [string]$Output,
    [ValidateRange(1,30)][int]$MaxMinutes=15,
    [ValidateRange(1,500)][int]$WarmupUpdates=200,
    [ValidateRange(1,2)][int]$EvalPairs=2
)
$ErrorActionPreference='Stop'
$aiRoot=Split-Path -Parent $PSScriptRoot
$aiDotnet=Join-Path $env:LOCALAPPDATA 'Goa2V1Toolchain/dotnet/dotnet.exe'
$aiPython=Join-Path $aiRoot '.venv-ai/Scripts/python.exe'
if(-not(Test-Path -LiteralPath $aiPython)){throw 'Prepare isolated .venv-ai first; see AI training setup.'}
if(-not $Output){$Output=Join-Path $aiRoot ('artifacts/ai-training/curriculum-'+[Guid]::NewGuid().ToString('N'))}
Push-Location $aiRoot
try {
    & $aiDotnet build ai/Goa2.Ai.Cli -c Release -t:Rebuild -p:EnableSourceLink=false -p:EmbedUntrackedSources=false -p:IncludeSourceRevisionInInformationalVersion=false -p:WarningsNotAsErrors=CS8602 -p:RestoreLockedMode=true
    if($LASTEXITCODE -ne 0){throw 'AI build failed'}
    if(-not $Data){
        $Data=$Output+'-data'
        & $aiDotnet ai/Goa2.Ai.Cli/bin/Release/net10.0/Goa2.Ai.Cli.dll curriculum $aiRoot ai/teaching-sources.json $Data
        if($LASTEXITCODE -ne 0){throw "Teaching export failed; preserve $Data"}
    }
    & $aiDotnet build-server shutdown
    & $aiPython ai/trainer/curriculum.py --data $Data --output $Output --max-minutes $MaxMinutes --warmup-updates $WarmupUpdates --eval-pairs $EvalPairs
    if($LASTEXITCODE -ne 0){throw "Bounded experiment failed; preserve $Output"}
} finally {Pop-Location}

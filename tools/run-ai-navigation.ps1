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
if(-not $Output){$Output=Join-Path $aiRoot ('artifacts/ai-training/navigation-'+[Guid]::NewGuid().ToString('N'))}
Push-Location $aiRoot
try {
    & $aiDotnet build ai/Goa2.Ai.Cli -c Release -t:Rebuild -p:EnableSourceLink=false -p:EmbedUntrackedSources=false -p:IncludeSourceRevisionInInformationalVersion=false -p:WarningsNotAsErrors=CS8602 -p:RestoreLockedMode=true
    if($LASTEXITCODE -ne 0){throw 'AI build failed'}
    & $aiDotnet build-server shutdown
    & $aiPython ai/trainer/defense.py --focus navigation --data docs/verification/ai-stage3-20261007/teaching-v3-01 docs/verification/ai-stage4-20261007/defense-data-01 docs/verification/ai-stage4-20261007/model-visited-data-01 --parent docs/verification/ai-stage4-20261007/defense-finetune-01/defense.pt --output $Output --updates $Updates --max-minutes $MaxMinutes --eval-seed 31001 --eval-pairs 2
    if($LASTEXITCODE -ne 0){throw "Bounded navigation experiment failed; preserve $Output"}
} finally {Pop-Location}

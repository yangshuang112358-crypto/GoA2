param(
    [ValidateSet('cpu','cuda')][string]$Device='cpu',
    [ValidateRange(1,10)][int]$Iterations=2,
    [ValidateRange(16,512)][int]$Rollout=128,
    [ValidateRange(1,4000)][int]$Steps=900,
    [ValidateRange(1,60)][int]$MaxMinutes=15,
    [string]$Output,
    [string]$Resume,
    [switch]$Test
)
$ErrorActionPreference='Stop'
$aiRoot=Split-Path -Parent $PSScriptRoot
$aiDotnet=Join-Path $env:LOCALAPPDATA 'Goa2V1Toolchain/dotnet/dotnet.exe'
$aiPython=Join-Path $aiRoot '.venv-ai/Scripts/python.exe'
if(-not(Test-Path -LiteralPath $aiPython)){throw 'Create the isolated .venv-ai environment using the training setup documentation first.'}
if(-not $Output){$Output=Join-Path $aiRoot ('artifacts/ai-training/'+[Guid]::NewGuid().ToString('N'))}
Push-Location $aiRoot
try {
    & $aiDotnet build ai/Goa2.Ai.Cli -c Release -t:Rebuild -p:EnableSourceLink=false -p:EmbedUntrackedSources=false -p:IncludeSourceRevisionInInformationalVersion=false -p:WarningsNotAsErrors=CS8602 -p:RestoreLockedMode=true
    if($LASTEXITCODE -ne 0){throw 'AI build failed'}
    if($Test){
        & $aiPython ai/trainer/test_trainer.py
        if($LASTEXITCODE -ne 0){throw 'Trainer tests failed'}
    }
    & $aiDotnet build-server shutdown
    $aiArguments=@('ai/trainer/train.py','--output',$Output,'--device',$Device,'--iterations',"$Iterations",'--rollout',"$Rollout",'--limit',"$Steps",'--max-minutes',"$MaxMinutes")
    if($Resume){$aiArguments+=@('--resume',$Resume)}
    & $aiPython @aiArguments
    if($LASTEXITCODE -ne 0){throw "Training verification failed. Preserve evidence: $Output"}
    Write-Output "Training evidence: $Output"
} finally {Pop-Location}

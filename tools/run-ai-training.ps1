param(
    [ValidateSet('cpu','cuda')][string]$Device='cpu',
    [ValidateSet('graph','entity','entity-gru')][string]$Architecture='entity',
    [ValidateRange(8,256)][int]$Hidden=96,
    [ValidateRange(1,128)][int]$Latents=32,
    [ValidateRange(4,512)][int]$MemoryDim=128,
    [ValidateRange(1,128)][int]$SequenceLength=16,
    [ValidateRange(1,128)][int]$BatchSize=32,
    [ValidateRange(1,5)][int]$Epochs=3,
    [ValidateRange(1,10)][int]$Iterations=2,
    [ValidateRange(16,512)][int]$Rollout=128,
    [ValidateRange(1,4000)][int]$Steps=900,
    [ValidateRange(1,4000)][int]$EvalSteps=1200,
    [int]$Seed=701,
    [int]$EvalSeed=51001,
    [ValidateRange(1,4)][int]$EvalPairs=1,
    [ValidateRange(1,100)][int]$Life=7,
    [ValidateRange(1,20)][int]$Marks=3,
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
        & $aiPython -m unittest discover -s ai/trainer -p 'test_*.py' -v
        if($LASTEXITCODE -ne 0){throw 'Trainer tests failed'}
    }
    & $aiDotnet build-server shutdown
    $aiArguments=@('ai/trainer/train.py','--output',$Output,'--device',$Device,'--iterations',"$Iterations",'--rollout',"$Rollout",'--limit',"$Steps",'--max-minutes',"$MaxMinutes")
    $aiArguments+=@('--architecture',$Architecture,'--hidden',"$Hidden",'--latents',"$Latents",'--memory-dim',"$MemoryDim",'--sequence-length',"$SequenceLength",'--batch-size',"$BatchSize",'--epochs',"$Epochs",'--eval-limit',"$EvalSteps",'--eval-seed',"$EvalSeed",'--eval-pairs',"$EvalPairs",'--seed',"$Seed",'--life',"$Life",'--marks',"$Marks")
    if($Resume){$aiArguments+=@('--resume',$Resume)}
    & $aiPython @aiArguments
    if($LASTEXITCODE -ne 0){throw "Training verification failed. Preserve evidence: $Output"}
    Write-Output "Training evidence: $Output"
} finally {Pop-Location}

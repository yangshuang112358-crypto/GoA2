param(
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$BatchId,
    [ValidateSet('Headless','Visual','Both')][string]$Mode='Headless',
    [ValidateRange(1,64)][int]$VisualIndex=1,
    [ValidateRange(0,10)][double]$Delay=0.1
)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaBatch=Join-Path $goaRoot ('artifacts/simulations/'+$BatchId)
$goaReports=@(Get-ChildItem -LiteralPath $goaBatch -Filter report.json -Recurse -File | Sort-Object { [int](Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).Seed })
if ($goaReports.Count -eq 0 -or $goaReports.Count -gt 64) { throw 'Expected 1 to 64 generated game reports.' }
if ($Mode -ne 'Headless' -and $VisualIndex -gt $goaReports.Count) { throw 'VisualIndex exceeds the available games.' }
$goaOutput=Join-Path $goaBatch ('player-replays/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $goaOutput -Force | Out-Null
$goaSummary=[ordered]@{batch=$BatchId;startedUtc=[DateTime]::UtcNow.ToString('o');finishedUtc=$null;passed=$false;error='';results=@()}
try {
    for ($goaIndex=0;$goaIndex -lt $goaReports.Count;$goaIndex++) {
        $goaReport=Get-Content -LiteralPath $goaReports[$goaIndex].FullName -Raw | ConvertFrom-Json
        if (-not $goaReport.Passed -or -not $goaReport.ScenarioReplayPassed) { throw 'Generation or scenario replay did not pass: '+$goaReports[$goaIndex].FullName }
        $goaSeed=[int]$goaReport.Seed
        $goaVariant=if ($goaReport.Sandbox) {'sandbox'} else {'formal'}
        $goaName='simulation-'+$goaSeed+'-'+$goaVariant
        $goaInput=Join-Path $goaReports[$goaIndex].DirectoryName ($goaName+'.json')
        $goaModes=@()
        if ($Mode -in @('Headless','Both')) { $goaModes+='headless' }
        if ($Mode -in @('Visual','Both') -and $goaIndex -eq ($VisualIndex-1)) { $goaModes+='visual' }
        foreach ($goaPlayerMode in $goaModes) {
            & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaInput -Visual:($goaPlayerMode -eq 'visual') -Delay $Delay -TimeoutSeconds 3600
            $goaActual=Get-Content -LiteralPath (Join-Path $goaRoot ('artifacts/scenarios/'+$goaName+'/'+$goaPlayerMode+'/latest.json')) -Raw | ConvertFrom-Json
            if ($goaActual.FinalStateHash -ne $goaReport.FinalStateHash) { throw "Native Player state differs for seed $goaSeed." }
            $goaSummary.results += [pscustomobject]@{seed=$goaSeed;mode=$goaPlayerMode;steps=$goaActual.Steps.Count;input=$goaInput;inputSha256=(Get-FileHash -LiteralPath $goaInput).Hash.ToLowerInvariant();stateHash=$goaActual.FinalStateHash}
        }
    }
    $goaSummary.passed=$true
} catch { $goaSummary.error=$_.Exception.Message; throw }
finally {
    $goaSummary.finishedUtc=[DateTime]::UtcNow.ToString('o')
    $goaSummary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaOutput 'player-replay.json') -Encoding utf8
    Write-Output "Player replay evidence: $goaOutput"
}

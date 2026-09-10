param(
    [ValidateRange(1,64)][int]$Games = 8,
    [ValidateRange(40,900)][int]$Steps = 400,
    [ValidateRange(0,1000000)][int]$Seed = 17,
    [switch]$ReplayPlayer,
    [switch]$Visual,
    [ValidateRange(1,64)][int]$VisualIndex = 1,
    [ValidateRange(0,10)][double]$Delay = 0.1
)
$ErrorActionPreference = 'Stop'
if ($Visual -and $VisualIndex -gt $Games) { throw 'VisualIndex exceeds Games.' }
$goaRoot = Split-Path -Parent $PSScriptRoot
$goaRunId = [Guid]::NewGuid().ToString('N')
$goaOutput = Join-Path $goaRoot ('artifacts/simulations/'+$goaRunId)
New-Item -ItemType Directory -Path $goaOutput | Out-Null
$goaSettings = @{ GOA_SIM_GAMES="$Games"; GOA_SIM_STEPS="$Steps"; GOA_SIM_SEED="$Seed"; GOA_SIM_RUN=$goaRunId }
$goaPrevious = @{}
$goaMetadata = [ordered]@{
    schemaVersion=1; id=$goaRunId; startedUtc=[DateTime]::UtcNow.ToString('o'); finishedUtc=$null
    commit=(& git -C $goaRoot rev-parse HEAD); worktreeStatus=@(& git -C $goaRoot status --porcelain --untracked-files=normal)
    games=$Games; stepLimit=$Steps; firstSeed=$Seed; passed=$false; error=''; results=@()
}
$goaManifest = Join-Path $goaOutput 'batch.json'
$goaMetadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $goaManifest -Encoding utf8
try {
    foreach ($goaName in $goaSettings.Keys) {
        $goaPrevious[$goaName]=[Environment]::GetEnvironmentVariable($goaName,'Process')
        [Environment]::SetEnvironmentVariable($goaName,$goaSettings[$goaName],'Process')
    }
    & "$PSScriptRoot/test-unity.ps1" -Filter Goa2.Tests.SimulationTests -ReportName ('simulations-'+$goaRunId)
    $goaReports = @(Get-ChildItem -LiteralPath $goaOutput -Filter report.json -File -Recurse | Sort-Object { (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).Seed })
    if ($goaReports.Count -ne $Games) { throw "Expected $Games fresh reports, got $($goaReports.Count)." }
    for ($goaIndex=0; $goaIndex -lt $goaReports.Count; $goaIndex++) {
        $goaReport = Get-Content -LiteralPath $goaReports[$goaIndex].FullName -Raw | ConvertFrom-Json
        if (-not $goaReport.Passed -or -not $goaReport.ScenarioReplayPassed) { throw "Seed $($goaReport.Seed) failed. Inspect $($goaReports[$goaIndex].FullName)" }
        $goaMode = if ($goaReport.Sandbox) { 'sandbox' } else { 'formal' }
        $goaScenarioName = 'simulation-'+$goaReport.Seed+'-'+$goaMode
        $goaScenario = Join-Path $goaReports[$goaIndex].DirectoryName ($goaScenarioName+'.json')
        $goaResult = [ordered]@{
            seed=$goaReport.Seed; mode=$goaMode; steps=$goaReport.Steps; round=$goaReport.FinalRound
            stopReason=$goaReport.StopReason; stateHash=$goaReport.FinalStateHash; scenario=$goaScenario
            inputSha256=(Get-FileHash -LiteralPath $goaScenario).Hash.ToLowerInvariant(); playerModes=@()
        }
        $goaModes = @()
        if ($ReplayPlayer) { $goaModes += 'headless' }
        if ($Visual -and $goaIndex -eq ($VisualIndex-1)) { $goaModes += 'visual' }
        foreach ($goaPlayerMode in $goaModes) {
            & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaScenario -Visual:($goaPlayerMode -eq 'visual') -Delay $Delay -TimeoutSeconds 3600
            $goaPlayerReport = Get-Content -LiteralPath (Join-Path $goaRoot ('artifacts/scenarios/'+$goaScenarioName+'/'+$goaPlayerMode+'/latest.json')) -Raw | ConvertFrom-Json
            if ($goaPlayerReport.FinalStateHash -ne $goaReport.FinalStateHash) { throw "Player state differs from simulation for seed $($goaReport.Seed)." }
            $goaResult.playerModes += $goaPlayerMode
        }
        $goaMetadata.results += [pscustomobject]$goaResult
    }
    $goaMetadata.passed=$true
} catch {
    $goaMetadata.error=$_.Exception.Message
    throw
} finally {
    foreach ($goaName in $goaPrevious.Keys) { [Environment]::SetEnvironmentVariable($goaName,$goaPrevious[$goaName],'Process') }
    $goaMetadata.finishedUtc=[DateTime]::UtcNow.ToString('o')
    $goaMetadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $goaManifest -Encoding utf8
    Write-Output "Simulation batch: $goaManifest"
}

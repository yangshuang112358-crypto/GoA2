param(
    [ValidatePattern('^[a-z0-9-]+$')][string]$Name='axe-cost',
    [switch]$List,
    [switch]$PrepareOnly,
    [switch]$ReplaceTrackedPlayer,
    [ValidateRange(1152,3840)][int]$Width=1600,
    [ValidateRange(768,2160)][int]$Height=1000
)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaPresets=@(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'debug-positions.json') -Raw | ConvertFrom-Json)
if ($List) { $goaPresets | Select-Object id,title,phase,pending; return }
$goaMatches=@($goaPresets | Where-Object { $_.id -ceq $Name })
if ($goaMatches.Count -ne 1) { throw 'Unknown or duplicate preset. Use -List to see available positions.' }
$goaPreset=$goaMatches[0]
if ($goaPreset.scenario -cnotmatch '^[a-z0-9-]+$' -or $goaPreset.through_step -isnot [long] -and $goaPreset.through_step -isnot [int] -or $goaPreset.through_step -lt 1) { throw 'Invalid preset scenario or step count.' }
if ($goaPreset.phase -notin @('HeroSelection','Deployment','Planning','InitiativeChoice','Action','EffectChoice','RoundEnd','Finished')) { throw 'Invalid expected phase.' }
if (-not $PrepareOnly) {
    if ($ReplaceTrackedPlayer) { . "$PSScriptRoot/ui-qa-common.ps1"; Close-QaPlayer }
    $goaPidPath=Join-Path $goaRoot 'artifacts/unity/player.pid'
    if (Test-Path -LiteralPath $goaPidPath) {
        $goaExisting=Get-Process -Id ([int](Get-Content -LiteralPath $goaPidPath)) -ErrorAction SilentlyContinue
        if ($goaExisting -and $goaExisting.ProcessName -eq 'Goa2V1') { throw 'A tracked Player is open. Close it or explicitly use -ReplaceTrackedPlayer.' }
    }
}
$goaSource=Join-Path $goaRoot ('tests/scenarios/'+$goaPreset.scenario+'.json')
$goaDefinition=Get-Content -LiteralPath $goaSource -Raw | ConvertFrom-Json
if ($goaPreset.through_step -gt $goaDefinition.Steps.Count) { throw 'Preset extends beyond its scenario.' }
$goaId='position-'+$Name+'-'+[Guid]::NewGuid().ToString('N')
$goaOutput=Join-Path $goaRoot ('artifacts/debug-positions/'+$goaId)
New-Item -ItemType Directory -Path $goaOutput | Out-Null
$goaDefinition.Id=$goaId; $goaDefinition.Name=$goaPreset.title
$goaDefinition.Steps=@($goaDefinition.Steps | Select-Object -First $goaPreset.through_step)
$goaInput=Join-Path $goaOutput ($goaId+'.json')
$goaDefinition | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $goaInput -Encoding utf8
# Rejected and duplicate steps remain in the input; only the actual save revision is used.
& "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaInput
$goaRunRoot=Join-Path $goaRoot ('artifacts/scenarios/'+$goaId+'/headless')
$goaRuns=@(Get-ChildItem -LiteralPath $goaRunRoot -Directory)
if ($goaRuns.Count -ne 1) { throw 'Expected exactly one fresh preparation run.' }
$goaReport=Get-Content -LiteralPath (Join-Path $goaRuns[0].FullName 'report.json') -Raw | ConvertFrom-Json
$goaSaveSource=Join-Path $goaRuns[0].FullName 'report.json.save.json'
$goaState=Get-Content -LiteralPath $goaSaveSource -Raw | ConvertFrom-Json
$goaLast=$goaReport.Steps | Select-Object -Last 1
$goaPending=if ($null -eq $goaState.Pending) { '' } else { $goaState.Pending.Kind }
if (-not $goaReport.Passed -or -not $goaReport.Complete -or $goaLast.Phase -cne $goaPreset.phase -or $goaPending -cne $goaPreset.pending) { throw 'Prepared position does not match its declared phase and pending choice.' }
$goaSave=Join-Path $goaOutput 'position.save.json'
Copy-Item -LiteralPath $goaSaveSource -Destination $goaSave
$goaPreferredSeat=if ($null -ne $goaState.Pending) { [int]$goaState.Pending.ChooserSeat } elseif ($null -ne $goaState.ActiveSeat) { [int]$goaState.ActiveSeat } else { 0 }
$goaEvidence=[ordered]@{
    id=$goaId;preset=$Name;title=$goaPreset.title;preparedUtc=[DateTime]::UtcNow.ToString('o')
    scenario=$goaPreset.scenario;throughStep=$goaPreset.through_step
    sourceSha256=(Get-FileHash -LiteralPath $goaSource).Hash.ToLowerInvariant()
    inputSha256=(Get-FileHash -LiteralPath $goaInput).Hash.ToLowerInvariant()
    stateHash=$goaReport.FinalStateHash;saveSha256=(Get-FileHash -LiteralPath $goaSave).Hash.ToLowerInvariant()
    engineVersion=$goaState.EngineVersion;revision=$goaState.Revision;phase=$goaLast.Phase;pending=$goaPending
    save=$goaSave;report=(Join-Path $goaRuns[0].FullName 'report.json');prepared=$true;opened=$false;preferredSeat=$goaPreferredSeat;openedSeat=$null
}
if (-not $PrepareOnly) {
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height -LoadSave $goaSave
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(20)
    $goaUi=$null
    do {
        Start-Sleep -Milliseconds 200
        try { $goaUi=Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json') -Raw | ConvertFrom-Json } catch { continue }
        if ($goaUi.Revision -eq $goaState.Revision -and $goaUi.Phase -ceq $goaLast.Phase) { $goaEvidence.opened=$true; break }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    if ($goaEvidence.opened) {
        & "$PSScriptRoot/qa-player.ps1" -Action Key -Key ([string]($goaPreferredSeat+1)) | Out-Null
        $goaUi=Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json') -Raw | ConvertFrom-Json
        $goaEvidence.openedSeat=$goaUi.Seat
        $goaEvidence.opened=$goaUi.Seat -eq $goaPreferredSeat -and $goaUi.Revision -eq $goaState.Revision
    }
}
$goaEvidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaOutput 'position.json') -Encoding utf8
if (-not $PrepareOnly -and -not $goaEvidence.opened) { throw 'Position prepared, but the fresh visible Player did not report the expected revision and phase.' }
Write-Output "Prepared: $($goaPreset.title), $($goaLast.Phase), revision $($goaState.Revision)."
Write-Output "Position: $goaSave"
Write-Output "Evidence: $goaOutput"

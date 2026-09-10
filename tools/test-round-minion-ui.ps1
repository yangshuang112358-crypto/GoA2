param([ValidateRange(1152,3840)][int]$Width=1600, [ValidateRange(768,2160)][int]$Height=1000)
$ErrorActionPreference = 'Stop'
$goaRoot = Split-Path -Parent $PSScriptRoot
$goaLayout = Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json'
$goaChecks = [System.Collections.Generic.List[object]]::new()
$goaStarted = [DateTime]::UtcNow
function Read-Ui { Get-Content -LiteralPath $goaLayout -Raw | ConvertFrom-Json }
function Check([bool]$Condition,[string]$Description) {
    $goaChecks.Add([pscustomobject]@{check=$Description;passed=$Condition})
    if (-not $Condition) { throw $Description }
    Write-Output "PASS $Description"
}
function Click([string]$Caption,[string]$Element) {
    if ($Element) { & "$PSScriptRoot/qa-player.ps1" -Action Click -Element $Element -AutoScroll | Out-Null }
    else { & "$PSScriptRoot/qa-player.ps1" -Action Click -Caption $Caption -AutoScroll | Out-Null }
}
function Save-State {
    Click '^保存$'
    Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Raw | ConvertFrom-Json
}
function Select-Cell([int]$X,[int]$Y) {
    $goaCell = (Read-Ui).Cells | Where-Object { $_.X -eq $X -and $_.Y -eq $Y }
    if (-not $goaCell -or -not $goaCell.Legal) { throw "Expected a legal target at $X,$Y" }
    & "$PSScriptRoot/qa-player.ps1" -Action Click -X ([int]$goaCell.Center.x) -Y ([int]$goaCell.Center.y) | Out-Null
}
try {
    $goaSetup = Get-Content -LiteralPath (Join-Path $goaRoot 'tests/scenarios/round-frontline.json') -Raw | ConvertFrom-Json
    $goaSetup.Id = 'ui-round-minion-setup'; $goaSetup.Name = '界面验收：轮末移兵、推进与升级恢复'; $goaSetup.Steps = @($goaSetup.Steps | Select-Object -First 8)
    $goaSetupPath = Join-Path $goaRoot 'artifacts/unity/ui-round-minion-setup.json'
    $goaSetup | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $goaSetupPath -Encoding utf8
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaSetupPath
    $goaSetupRun = Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/scenarios/ui-round-minion-setup/headless') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height -LoadSave (Join-Path $goaSetupRun.FullName 'report.json.save.json')
    $goaDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        try { $goaUi = Read-Ui } catch { continue }
        if ($goaUi.Phase -eq 'EffectChoice' -and $goaUi.Revision -eq 8) { break }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    Check ($goaUi.Phase -eq 'EffectChoice' -and $goaUi.Revision -eq 8) 'Pending round minion battle restores in the actual Player'
    Check (@($goaUi.Cells | Where-Object Legal).Count -eq 0) 'The other team cannot select losing-side minions'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Check (@((Read-Ui).Cells | Where-Object Legal).Count -eq 2) 'Captain sees only the two non-heavy minions'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'round-ui-minion-choice' | Out-Null
    Select-Cell 3 0; Click '^确认移除 '
    $goaState = Save-State
    Check ($goaState.RoundEnd.RemainingRemovals -eq 2 -and $goaState.Players[1].Gold -eq 0) 'First minion removal leaves two choices and awards no gold'
    Select-Cell -1 1; Click '^确认移除 '
    $goaState = Save-State
    Check ($goaState.RoundEnd.RemainingRemovals -eq 1 -and @((Read-Ui).Cells | Where-Object Legal).Count -eq 1) 'Heavy becomes the only legal choice after both non-heavy minions'
    Select-Cell -3 0; Click '^确认移除 '
    $goaState = Save-State
    Check ($goaState.Pending.Kind -eq 'minion_spawn' -and $goaState.RoundEnd.RemainingRemovals -eq 0 -and $goaState.Players[0].Gold -eq 6 -and $goaState.Players[0].Level -eq 1) 'Heavy loss pauses for new births before charging any upgrades'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'round-ui-birth' | Out-Null
    $goaSavedRevision = $goaState.Revision
    Click '^读取$'
    Check ((Read-Ui).Revision -eq $goaSavedRevision -and (Read-Ui).Phase -eq 'EffectChoice') 'Birth interruption restores with its round settlement context'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Home | Out-Null
    Select-Cell 1 -7; Click '^确认小兵出生于 '
    $goaState = Save-State
    Check ($goaState.Phase -eq 5 -and $goaState.RoundEnd.Stage -eq 'upgrades' -and $goaState.Players[0].Level -eq 4 -and $goaState.Players[0].Gold -eq 0 -and $goaState.Units.Count -eq 15) 'Final birth resumes upgrading exactly once and preserves all eleven new minions'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'upgrade-card-*' -and $_.Visible }).Count -eq 2) 'Correct player receives their private upgrade candidates after birth'
    Click -Element 'upgrade-card-wasp-03-电能波'; Click '^确认升级为 电能波$'
    Click -Element 'upgrade-card-wasp-08-偏转屏障'; Click '^确认升级为 偏转屏障$'
    Click -Element 'upgrade-card-wasp-15-引力控制'; Click '^确认升级为 引力控制$'
    $goaState = Save-State
    Check ($goaState.Round -eq 2 -and $goaState.Phase -eq 2 -and $goaState.Units.Count -eq 15 -and $goaState.BlueMarks -eq 1 -and $goaState.Players[0].Gold -eq 0 -and $goaState.Players[1].Gold -eq 1) 'Completing upgrades starts the next round without applying the old casualty count to new minions'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'round-ui-next-round' | Out-Null
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaReport = Join-Path $goaRoot "artifacts/unity/round-minion-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified headless setup followed by real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

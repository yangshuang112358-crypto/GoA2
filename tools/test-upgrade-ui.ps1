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
try {
    $goaSetup = Get-Content -LiteralPath (Join-Path $goaRoot 'tests/scenarios/round-upgrades.json') -Raw | ConvertFrom-Json
    $goaSetup.Id = 'ui-upgrade-setup'; $goaSetup.Name = '界面验收：轮末与各角色升级'; $goaSetup.Steps = @($goaSetup.Steps | Select-Object -First 4)
    $goaSetupPath = Join-Path $goaRoot 'artifacts/unity/ui-upgrade-setup.json'
    $goaSetup | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $goaSetupPath -Encoding utf8
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaSetupPath
    $goaSetupRun = Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/scenarios/ui-upgrade-setup/headless') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height -LoadSave (Join-Path $goaSetupRun.FullName 'report.json.save.json')
    $goaDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        try { $goaUi = Read-Ui } catch { continue }
        if ($goaUi.Phase -eq 'RoundEnd' -and $goaUi.Revision -eq 4) { break }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    Check ($goaUi.Phase -eq 'RoundEnd' -and $goaUi.Revision -eq 4) 'Round-end fixture restores in the actual Player'
    Check ($goaUi.FilledPlayDots -eq 16) 'All four round slots remain filled before settlement'
    $goaRevealedHeading = $goaUi.RevealedHeading
    Click -Element 'resolve-round-end'
    $goaState = Save-State
    Check ($goaState.RoundEnd.Stage -eq 'upgrades' -and $goaState.Players[0].Level -eq 4 -and $goaState.Players[1].Level -eq 2 -and $goaState.Players[0].Gold -eq 0) 'Settlement recalls cards and spends six gold for three levels'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'upgrade-card-*' -and $_.Visible }).Count -eq 2) 'Selected color presents two large candidate cards'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 3 | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'upgrade-card-*' }).Count -eq 0) 'A seat without an upgrade sees no other player candidates'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Click -Element 'upgrade-card-wasp-03-电能波'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'upgrade-ui-candidates' | Out-Null
    Click '^确认升级为 电能波$'
    $goaState = Save-State
    Check ($goaState.Players[0].UpgradeHistory.Count -eq 1 -and $goaState.Players[0].InitiativeBonus -eq 1 -and $goaState.Players[0].DefenseBonus -eq 0) 'Choosing a card grants the rejected candidate initiative icon'
    Check (@((Read-Ui).Buttons | Where-Object Name -eq 'upgrade-color-red').Count -eq 0) 'Red cannot reach tier three before the other colors reach tier two'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Click -Element 'upgrade-card-shargatha-02-快速突刺'
    Click '^确认升级为 快速突刺$'
    $goaState = Save-State
    Check ($goaState.Round -eq 1 -and $goaState.Players[1].UpgradeHistory.Count -eq 1 -and $goaState.RoundEnd.Upgrades[0].PendingLevels.Count -eq 2) 'Another player completes their upgrade while the first is still choosing'
    $goaSavedRevision = $goaState.Revision
    Click '^读取$'
    Check ((Read-Ui).Revision -eq $goaSavedRevision -and (Read-Ui).Phase -eq 'RoundEnd') 'Saved parallel upgrade progress restores in the window'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Click -Element 'upgrade-card-wasp-08-偏转屏障'; Click '^确认升级为 偏转屏障$'
    Click -Element 'upgrade-card-wasp-15-引力控制'; Click '^确认升级为 引力控制$'
    $goaState = Save-State; $goaUi = Read-Ui
    Check ($goaState.Round -eq 2 -and $goaState.Turn -eq 1 -and $goaState.Phase -eq 2 -and $null -eq $goaState.RoundEnd -and @($goaState.Players | Where-Object { @($_.Cards | Where-Object Zone -eq 0).Count -eq 5 }).Count -eq 4) 'All choices complete and the next round starts with five cards each'
    Check ($goaState.Players[0].Gold -eq 0 -and $goaState.Players[1].Gold -eq 0 -and $goaState.Players[2].Gold -eq 1 -and $goaState.Players[3].Gold -eq 1) 'Only players who did not upgrade receive compensation'
    Check ($goaUi.FilledPlayDots -eq 0 -and $goaUi.DiscardDotCount -eq 0 -and $goaUi.RevealedHeading -eq $goaRevealedHeading) 'New round clears color slots while retaining the latest revealed cards'
    Click -Element 'bonus-sources'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'upgrade-ui-next-round' | Out-Null
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaReport = Join-Path $goaRoot "artifacts/unity/upgrade-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified headless setup followed by real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

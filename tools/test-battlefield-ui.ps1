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
    $goaSetup = Get-Content -LiteralPath (Join-Path $goaRoot 'tests/scenarios/frontline.json') -Raw | ConvertFrom-Json
    $goaSetup.Id = 'ui-frontline-setup'; $goaSetup.Name = '界面验收：等待队长安排出生'; $goaSetup.Steps = @($goaSetup.Steps | Select-Object -First 3)
    $goaSetupPath = Join-Path $goaRoot 'artifacts/unity/ui-frontline-setup.json'
    $goaSetup | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $goaSetupPath -Encoding utf8
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaSetupPath
    $goaSetupRun = Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/scenarios/ui-frontline-setup/headless') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height -LoadSave (Join-Path $goaSetupRun.FullName 'report.json.save.json')
    $goaDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        try { $goaUi = Read-Ui } catch { continue }
        if ($goaUi.Phase -eq 'EffectChoice' -and $goaUi.Revision -eq 3) { break }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    Check ($goaUi.Phase -eq 'EffectChoice' -and $goaUi.Revision -eq 3) 'Pending spawn restores in the interactive Player'
    Check (@($goaUi.Cells | Where-Object Legal).Count -eq 0) 'Wrong seat sees no selectable spawn hexes'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Home | Out-Null
    $goaUi = Read-Ui
    Check ([Math]::Abs($goaUi.Zoom - 1) -lt 0.001) 'Home fits the entire map before choosing an off-center target'
    Check ($goaUi.Seat -eq 1 -and @($goaUi.Cells | Where-Object Legal).Count -eq 3) 'Captain shortcut exposes three legal adjacent cells'
    $goaCell = $goaUi.Cells | Where-Object { $_.X -eq 1 -and $_.Y -eq -7 }
    & "$PSScriptRoot/qa-player.ps1" -Action Click -X ([int]$goaCell.Center.x) -Y ([int]$goaCell.Center.y) | Out-Null
    Check ((Read-Ui).SelectedCell -eq '1,-7') 'Click selects the intended spawn hex'
    Click '^确认小兵出生于'
    $goaState = Save-State
    $goaSpawned = $goaState.Units | Where-Object Id -eq 'minion:1:2,-7'
    Check ($goaState.Phase -eq 2 -and $goaSpawned.Position.X -eq 1 -and $goaSpawned.Position.Y -eq -7 -and $goaState.Units.Count -eq 15) 'Confirmation spawns the minion and resumes planning'
    Check ($goaState.Players[0].Gold -eq 4) 'Resuming does not duplicate the kill reward'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'battlefield-ui-spawn' | Out-Null
    Click '^调试$'; Click -Element 'debug-remove-red-heavy'
    $goaState = Save-State
    Check ($goaState.Phase -eq 7 -and $goaState.Winner -eq 0 -and $goaState.VictoryReason -eq 'fountain' -and $goaState.BlueMarks -eq 2 -and $goaState.Units.Count -eq 4) 'Debug removal immediately triggers fountain victory without new minions'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'debug-remove-*' -and $_.Enabled }).Count -eq 0) 'Finished match presents results without active removal controls'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'battlefield-ui-victory' | Out-Null
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaReport = Join-Path $goaRoot "artifacts/unity/battlefield-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified headless setup followed by real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

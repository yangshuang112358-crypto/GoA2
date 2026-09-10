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
    $goaSetup = Get-Content -LiteralPath (Join-Path $goaRoot 'tests/scenarios/combat-defense.json') -Raw | ConvertFrom-Json
    $goaSetup.Id = 'ui-combat-setup'; $goaSetup.Name = '界面验收：攻击、防御与复活'; $goaSetup.Steps = @($goaSetup.Steps | Select-Object -First 7)
    $goaSetupPath = Join-Path $goaRoot 'artifacts/unity/ui-combat-setup.json'
    $goaSetup | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $goaSetupPath -Encoding utf8
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaSetupPath
    $goaSetupRun = Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/scenarios/ui-combat-setup/headless') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height -LoadSave (Join-Path $goaSetupRun.FullName 'report.json.save.json')
    $goaDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        try { $goaUi = Read-Ui } catch { continue }
        if ($goaUi.Phase -eq 'Action' -and $goaUi.Revision -eq 7) { break }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    Check ($goaUi.Phase -eq 'Action' -and $goaUi.Revision -eq 7) 'Combat fixture restores with the attacker active'
    Click -Element 'begin-primary'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Home | Out-Null
    $goaState = Save-State
    Check ($goaState.Pending.Kind -eq 'attack_target' -and $goaState.Pending.ChooserSeat -eq 0) 'Begin primary action exposes the authoritative target choice'
    Select-Cell 8 -9
    Click '^确认攻击 '
    $goaState = Save-State
    Check ($goaState.Pending.Kind -eq 'defense' -and $goaState.Pending.ChooserSeat -eq 1) 'Map target confirmation enters the defender response'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Text -match '^闪耀之刃 ·' -and $_.Visible }).Count -eq 0) 'Attacker cannot access the other seat defense options'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'combat-ui-attack' | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Check ((Read-Ui).Seat -eq 1) 'Shortcut switches directly to the defender'
    Click -Element 'hand-gold'
    Click '^确认使用 闪耀之刃 防御$'
    $goaState = Save-State
    Check ($goaState.Pending.Kind -eq 'hero_respawn' -and $goaState.Pending.ChooserSeat -eq 1 -and $goaState.Players[1].AwaitingRespawn -and @($goaState.Units | Where-Object Id -eq 'hero:1').Count -eq 0) 'Failed numeric defense removes the hero and waits for its next-action respawn'
    Check ($goaState.Players[0].Gold -eq 1 -and $goaState.Players[2].Gold -eq 1 -and $goaState.RedCrystal -eq 6) 'Defeat awards killer and ally gold and deducts crystals once'
    Check (@($goaState.Players[1].Cards | Where-Object { $_.CardId -eq 'wasp-00-闪耀之刃' -and $_.Zone -eq 4 }).Count -eq 1) 'The selected defense card enters the discard zone'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'combat-ui-defeat' | Out-Null
    Select-Cell 7 -9
    Click '^确认复活于 '
    $goaState = Save-State
    $goaHero = $goaState.Units | Where-Object Id -eq 'hero:1'
    Check ($goaState.Phase -eq 4 -and $goaState.ActiveSeat -eq 1 -and -not $goaState.Players[1].AwaitingRespawn -and $goaHero.Position.X -eq 7 -and $goaHero.Position.Y -eq -9) 'Respawn resumes the already revealed defender card'
    Check ($goaState.RedCrystal -eq 6 -and $goaState.Players[0].Gold -eq 1) 'Respawn does not duplicate crystal damage or gold'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'combat-ui-respawn' | Out-Null
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaReport = Join-Path $goaRoot "artifacts/unity/combat-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified headless setup followed by real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

param([ValidateRange(1152,3840)][int]$Width=1600, [ValidateRange(768,2160)][int]$Height=1000)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[System.Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
function Read-Ui { Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json') -Raw | ConvertFrom-Json }
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
function Close-QaPlayer {
    $goaPidFile=Join-Path $goaRoot 'artifacts/unity/player.pid'
    if (-not (Test-Path -LiteralPath $goaPidFile)) { return }
    $goaProcess=Get-Process -Id ([int](Get-Content -LiteralPath $goaPidFile)) -ErrorAction SilentlyContinue
    if (-not $goaProcess -or $goaProcess.ProcessName -ne 'Goa2V1') { return }
    if ($goaProcess.Path -ne (Join-Path $goaRoot 'artifacts/player/Goa2V1.exe')) { throw 'The tracked process is outside the QA Player path.' }
    [void]$goaProcess.CloseMainWindow()
    if (-not $goaProcess.WaitForExit(5000)) { throw 'The QA Player did not close.' }
}
function Open-Setup([string]$Scenario,[int]$Steps=7) {
    Close-QaPlayer
    $goaSetup=Get-Content -LiteralPath (Join-Path $goaRoot "tests/scenarios/$Scenario.json") -Raw | ConvertFrom-Json
    $goaSetup.Id="ui-$Scenario-setup"
    $goaSetup.Steps=@($goaSetup.Steps | Select-Object -First $Steps)
    $goaSetupPath=Join-Path $goaRoot "artifacts/unity/$($goaSetup.Id).json"
    $goaSetup | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $goaSetupPath -Encoding utf8
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaSetupPath
    $goaRun=Get-ChildItem -LiteralPath (Join-Path $goaRoot "artifacts/scenarios/$($goaSetup.Id)/headless") -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height -LoadSave (Join-Path $goaRun.FullName 'report.json.save.json')
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        try { $goaUi=Read-Ui } catch { continue }
        if ($goaUi.Phase -eq 'Action' -and $goaUi.Revision -eq $Steps) { break }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    Check ($goaUi.Phase -eq 'Action' -and $goaUi.Revision -eq $Steps) "$Scenario setup restores with its action ready"
}
function Pass-Seat([int]$Key) {
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key $Key | Out-Null
    Click '^放弃此牌行动$'
    Click '^确认放弃此牌行动$'
}
try {
    Open-Setup 'static-field'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -eq 'begin-primary' -and $_.Text -eq '执行基础技能' -and $_.Enabled }).Count -eq 1) 'The skill has an enabled skill action button'
    Click -Element 'begin-primary'
    $goaState=Save-State
    Check ($goaState.Effects.Count -eq 1 -and $goaState.Effects[0].SourceCardId -eq 'wasp-06-静电封锁' -and $goaState.ActiveSeat -eq 1) 'Executing the skill creates the sourced aura and continues the next hero'
    $goaRevision=(Read-Ui).Revision
    Click -Element 'effect-area-effect:1'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Home | Out-Null
    Check ((Read-Ui).EffectAreaId -eq 'effect:1' -and (Read-Ui).EffectAreaCells -gt 0 -and (Read-Ui).Revision -eq $goaRevision) 'Viewing the aura paints its core-provided area without changing the match'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'aura-ui-static-area' | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Click '^次要移动'
    $goaUi=Read-Ui
    Check (@($goaUi.Cells | Where-Object { $_.X -eq 8 -and $_.Y -eq -8 -and $_.Legal }).Count -eq 0) 'The adjacent outside hex is not offered as a move'
    $goaCell=$goaUi.Cells | Where-Object { $_.X -eq 8 -and $_.Y -eq -10 }
    Check ($goaCell.Legal) 'An inside hex remains a legal movement target'
    & "$PSScriptRoot/qa-player.ps1" -Action Click -X ([int]$goaCell.Center.x) -Y ([int]$goaCell.Center.y) | Out-Null
    Click '^确认移动至 '
    $goaState=Save-State
    $goaHero=$goaState.Units | Where-Object Id -eq 'hero:1'
    Check ($goaHero.Position.X -eq 8 -and $goaHero.Position.Y -eq -10 -and $goaState.ActiveSeat -eq 2) 'Map confirmation moves only inside the boundary and completes the card'
    Pass-Seat 3
    Pass-Seat 4
    $goaState=Save-State
    Check ($goaState.Turn -eq 2 -and $goaState.Effects.Count -eq 0 -and (Read-Ui).EffectAreaId -eq '' -and (Read-Ui).EffectAreaCells -eq 0) 'Turn end removes the expired effect and its displayed area'
    Check ((Read-Ui).RevealedHeading -match '1轮1回合') 'Previously revealed cards remain visible after the aura expires'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'aura-ui-expired' | Out-Null

    Open-Setup 'skill-suppression'
    Click -Element 'begin-primary'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    $goaUi=Read-Ui
    Check (@($goaUi.Buttons | Where-Object { $_.Name -eq 'begin-primary' -and -not $_.Enabled }).Count -eq 1 -and $goaUi.PrimaryRestriction -eq 'arien-06-打断施法') 'The blocked skill is disabled with its actual source exposed to the view'
    Click -Element 'effect-area-effect:1'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Home | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'aura-ui-silenced' | Out-Null
    $goaState=Save-State
    Check ($goaState.ActiveSeat -eq 1 -and $goaState.Effects.Count -eq 1) 'Inspecting a blocked skill and its area does not consume the card'
    Pass-Seat 2
    Check ((Read-Ui).ActiveSeat -eq 2) 'A silenced hero can still pass its selected card'
    Pass-Seat 3
    Pass-Seat 4
    Check ((Read-Ui).ActiveEffectCount -eq 0 -and (Read-Ui).PrimaryRestriction -eq '' -and (Read-Ui).Turn -eq 2) 'Skill suppression and its warning clear at the turn boundary'

    Open-Setup 'shining-blade' 9
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -eq 'begin-primary' -and $_.Text -eq '执行基础攻击' -and $_.Enabled }).Count -eq 1) 'Skill suppression leaves Shining Blade basic attack available'
    Click -Element 'effect-area-effect:1'
    Click -Element 'begin-primary'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Home | Out-Null
    $goaCell=(Read-Ui).Cells | Where-Object { $_.X -eq 8 -and $_.Y -eq -10 }
    Check ($goaCell.Legal) 'The adjacent enemy hero is offered as the basic attack target'
    & "$PSScriptRoot/qa-player.ps1" -Action Click -X ([int]$goaCell.Center.x) -Y ([int]$goaCell.Center.y) | Out-Null
    Click '^确认攻击 '
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Click -Element 'hand-blue'
    Click '^确认使用 挑战者 防御$'
    $goaState=Save-State
    Check ($goaState.Effects.Count -eq 1 -and $goaState.Effects[0].SourceCardId -eq 'wasp-00-闪耀之刃' -and $goaState.Effects[0].AreaKind -eq 1) 'Successful defense is followed by a sourced adjacent silence'
    Check ((Read-Ui).EffectAreaId -eq '' -and @($goaState.Events | Where-Object { $_.Kind -eq 'EffectCancelled' -and $_.CardId -eq 'arien-06-打断施法' }).Count -eq 1) 'The cancelled old skill and its displayed area are removed'
    Click -Element 'effect-area-effect:2'
    Check ((Read-Ui).EffectAreaCells -eq 4) 'The new adjacent area uses one hex distance at this map edge'
    $goaRevision=(Read-Ui).Revision
    Click -Element 'focus-hero'
    $goaUi=Read-Ui
    $goaCell=$goaUi.Cells | Where-Object { $_.X -eq 8 -and $_.Y -eq -10 }
    Check ($goaUi.HexRadius -ge 19.9 -and [math]::Abs($goaCell.Center.x-($goaUi.BoardBounds.x+$goaUi.BoardBounds.width/2)) -lt 1 -and [math]::Abs($goaCell.Center.y-($goaUi.BoardBounds.y+$goaUi.BoardBounds.height/2)) -lt 1 -and $goaUi.Revision -eq $goaRevision) 'Locate hero centers the controlled character at a readable scale without a game command'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'blade-ui-cancelled' | Out-Null
    Pass-Seat 3
    Pass-Seat 4
    Check ((Read-Ui).Turn -eq 2 -and (Read-Ui).ActiveEffectCount -eq 0) 'Shining Blade silence expires after all remaining heroes finish'
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaReport=Join-Path $goaRoot "artifacts/unity/auras-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified scenarios followed by real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

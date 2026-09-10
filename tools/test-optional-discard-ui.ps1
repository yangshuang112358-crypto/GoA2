param([ValidateRange(1152,3840)][int]$Width=1600,[ValidateRange(768,2160)][int]$Height=1000)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[System.Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
function Focus-Targets {
    if ((Read-Ui).TopExpanded) { Click -Element 'toggle-top' }
    Click -Element 'focus-hero'
}
try {
    Open-Setup 'throwing-axe-reflection' 10
    Click -Element 'begin-primary'
    $goaState=Save-State
    Check ($goaState.Pending.Kind -eq 'optional_discard' -and $goaState.Pending.Optional) 'Axe starts with an explicit optional discard before choosing targets'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'optional-discard-*' -and $_.Enabled }).Count -eq 5) 'Four hand choices and one explicit skip are available'
    Check (@((Read-Ui).Cells | Where-Object Legal).Count -eq 0) 'Attack targets are not selectable during the cost choice'
    $goaPending=Join-Path $goaRoot 'artifacts/unity/optional-ui-pending.json'
    Copy-Item -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Destination $goaPending -Force
    $goaRevision=$goaState.Revision
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'optional-discard-*' }).Count -eq 0) 'Other seats cannot select or skip the attacker cost'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Click -Element 'hand-gold'; Click '^取消$'
    $goaState=Save-State
    Check ($goaState.Revision -eq $goaRevision -and $goaState.Pending.Kind -eq 'optional_discard') 'Cancel clears only local selection and keeps the optional choice pending'
    Click -Element 'hand-gold'; Click '^确认弃置 猛攻$'
    Focus-Targets
    $goaState=Save-State
    Check ($goaState.Pending.Kind -eq 'attack_target' -and $goaState.Execution.PreAttackDiscarded -and $goaState.Execution.AttackRangeBonus -eq 2) 'Confirming the cost locks a plus two attack distance'
    Check (@($goaState.Players[0].Cards | Where-Object Zone -eq 4).Count -eq 1 -and (Read-Ui).DiscardDotCount -eq 1) 'The single paid card produces one discard dot'
    Check (@((Read-Ui).Labels | Where-Object { $_.Name -eq 'attack-range' -and $_.Text -eq '本次攻击距离 3' }).Count -eq 1) 'The target choice states the effective distance three'
    $goaCell=(Read-Ui).Cells | Where-Object { $_.X -eq 6 -and $_.Y -eq -5 }
    Check ($goaCell.Legal) 'The enemy exactly three hexes away is highlighted'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'optional-axe-paid' | Out-Null
    Open-Save $goaPending 'EffectChoice' $goaRevision
    Click -Element 'optional-discard-gold'; Click '^确认弃置 猛攻$'
    Focus-Targets; Select-Cell 6 -5; Click '^确认攻击 '
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Click -Element 'hand-green'; Click '^确认使用 反射屏障 防御$'
    $goaState=Save-State
    Check ($goaState.Pending.Kind -eq 'forced_discard' -and $goaState.Execution.AttackRangeBonus -eq 2) 'Reflection keeps the paid range and opens a separate mandatory discard'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -eq 'forced-discard-gold' -or $_.Name -eq 'optional-discard-skip' }).Count -eq 0) 'The paid card cannot be discarded again and the mandatory counter has no skip'
    Click -Element 'hand-silver'; Click '^确认弃置 铜墙铁壁$'
    $goaState=Save-State
    Check (@($goaState.Events | Where-Object Kind -eq 'AttackResolved').Count -eq 1 -and @($goaState.Events | Where-Object Kind -eq 'OptionalDiscardCompleted').Count -eq 1) 'Restoring then finishing causes one cost and one attack resolution'
    Check ((Read-Ui).DiscardDotCount -eq 3 -and $goaState.Effects.Count -eq 1) 'Two attacker discards and one defense discard are shown beside the active immunity'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'optional-axe-reflection' | Out-Null

    Open-Setup 'throwing-spear-existing-discard' 9
    Click -Element 'begin-primary'; Click -Element 'optional-discard-skip'
    Focus-Targets; $goaState=Save-State
    Check ($goaState.Pending.Kind -eq 'attack_target' -and -not $goaState.Execution.PreAttackDiscarded -and $goaState.Execution.AttackRangeBonus -eq 2) 'Spear retains plus two after explicitly skipping with an existing discard'
    Check (@($goaState.Players[0].Cards | Where-Object Zone -eq 0).Count -eq 3 -and (Read-Ui).DiscardDotCount -eq 1) 'Skipping leaves the three hand cards and existing discard unchanged'
    Select-Cell 6 -5; Click '^确认攻击 '
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Click -Element 'hand-green'; Click '^确认使用 抵挡屏障 防御$'
    $goaState=Save-State
    Check ($null -eq $goaState.Execution -and @($goaState.Events | Where-Object Kind -eq 'OptionalDiscardCompleted').Count -eq 0) 'The skipped-cost attack can be blocked without creating an attacker discard'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'optional-spear-skipped' | Out-Null

    Open-Setup 'throwing-axe-skip' 10
    Click -Element 'begin-primary'; Click -Element 'optional-discard-skip'
    $goaState=Save-State
    Check ($null -eq $goaState.Execution -and $goaState.ActiveSeat -eq 3) 'Axe without a new cost ends when no adjacent enemy target exists'
    Check (@($goaState.Events | Where-Object Kind -eq 'AttackDeclared').Count -eq 0 -and @($goaState.Players[0].Cards | Where-Object Zone -eq 4).Count -eq 1) 'The no-target branch creates no attack and preserves the prior discard'
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaReport=Join-Path $goaRoot "artifacts/unity/optional-discard-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified scenarios followed by real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

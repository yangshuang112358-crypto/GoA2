param([ValidateRange(1280,3840)][int]$Width=1280,[ValidateRange(800,2160)][int]$Height=800)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
try {
    Open-Setup 'riposte-hand' 8
    Click -Element 'begin-primary'
    if((Read-Ui).TopExpanded) { Click -Element 'toggle-top' }
    Click -Element 'focus-hero';Select-Cell 8 -10
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    Check ((Read-Ui).Revision -eq 10) 'Physical Space confirms the chosen attack target'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Click '^近身还击 · 抵挡$'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    Check ((Read-Ui).Revision -eq 11) 'The defender confirms riposte using the available defense option'
    Check (@((Read-Ui).Buttons | Where-Object Name -eq 'retaliation-decline').Count -eq 0) 'The defender cannot choose the attacker defeat branch'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    $goaPending=Save-State
    Check ($goaPending.EngineVersion -ge 10 -and $goaPending.Pending.Kind -eq 'forced_discard' -and $goaPending.Pending.ChooserSeat -eq 0) 'The original attacker owns the restored counter choice'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -eq 'retaliation-decline' -and $_.Enabled }).Count -eq 1) 'Discard and explicit defeat are both available with cards in hand'
    $goaPendingPath=Join-Path $goaRoot 'artifacts/unity/riposte-ui-pending.json'
    Copy-Item -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Destination $goaPendingPath -Force

    # The glossary is a local overlay: physical keyboard shortcuts must not execute game commands.
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key F1 | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object Name -eq 'keyword-close').Count -eq 1) 'Physical F1 opens the glossary'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 4 | Out-Null
    Check ((Read-Ui).Revision -eq $goaPending.Revision -and (Read-Ui).Seat -eq 0) 'Space and seat keys do not act behind the glossary'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Escape | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object Name -eq 'keyword-close').Count -eq 0) 'Physical Escape closes the glossary'

    Click -Element 'retaliation-decline';Click '^取消$'
    Check ((Save-State).Revision -eq $goaPending.Revision) 'Cancelling the defeat preview leaves the counter pending'
    Click -Element 'retaliation-decline';Click -Element 'forced-discard-red'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Text -eq '确认不弃牌并被击败' }).Count -eq 0) 'Choosing a hand card clears the defeat preview'
    Click '^确认弃置 电击$'
    $goaDiscarded=Save-State
    Check ($goaDiscarded.BlueCrystal -eq 7 -and -not $goaDiscarded.Players[0].AwaitingRespawn -and @($goaDiscarded.Players[0].Cards | Where-Object Zone -eq 4).Count -eq 1) 'The card-discard route keeps the hero alive and discards exactly one card'

    Open-Save $goaPendingPath 'EffectChoice' $goaPending.Revision
    Click -Element 'retaliation-decline'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'riposte-decline-choice' | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    $goaDefeated=Save-State
    Check ($goaDefeated.BlueCrystal -eq 6 -and $goaDefeated.Players[0].AwaitingRespawn -and @($goaDefeated.Players[0].Cards | Where-Object Zone -eq 0).Count -eq 4) 'Space confirms defeat without discarding any attacker hand card'
    Check ($goaDefeated.Players[1].Gold -eq 1 -and $goaDefeated.Players[3].Gold -eq 1 -and @($goaDefeated.Events | Where-Object Kind -eq 'AttackResolved').Count -eq 1 -and $null -eq $goaDefeated.Execution) 'Rewards and the original attack complete once after restoring'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'riposte-decline-result' | Out-Null

    Click '^调试$';Click -Element 'open-debug-presets';Click -Element 'debug-presets-filter'
    & "$PSScriptRoot/qa-text.ps1" -Text '近身还击' | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'debug-preset-*' }).Count -eq 3) 'Typing filters the scrollable preset list to three riposte situations'
    Click -Element 'debug-preset-riposte-victory';Click -Element 'debug-presets-confirm'
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 150
        $goaLoaded=Read-Ui
        if($goaLoaded.Phase -eq 'EffectChoice' -and $goaLoaded.Seat -eq 0 -and @($goaLoaded.Buttons | Where-Object Name -eq 'debug-presets-confirm').Count -eq 0) { break }
    } while([DateTime]::UtcNow -lt $goaDeadline)
    Check ((Read-Ui).Phase -eq 'EffectChoice' -and (Read-Ui).Seat -eq 0) 'Loading the critical-crystal preset selects the original attacker'
    Click -Element 'retaliation-decline'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    $goaVictory=Save-State
    Check ($goaVictory.Phase -eq 7 -and $goaVictory.BlueCrystal -eq 0 -and $goaVictory.Winner -eq 1 -and $null -eq $goaVictory.Pending -and $null -eq $goaVictory.Execution) 'Choosing defeat at one crystal ends the match immediately'

    Open-Save (Join-Path $goaRoot 'tests/fixtures/engine9-riposte-hand.json') 'EffectChoice' 11
    Check (@((Read-Ui).Buttons | Where-Object Name -eq 'retaliation-decline').Count -eq 0) 'A real engine nine save retains mandatory discard with no new defeat option'
    Click -Element 'forced-discard-red';Click '^确认弃置 电击$'
    $goaLegacy=Save-State
    Check ($goaLegacy.EngineVersion -eq 9 -and $goaLegacy.BlueCrystal -eq 7 -and $null -eq $goaLegacy.Execution) 'The original engine nine choice still completes without defeat'
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message});throw
} finally {
    $goaReport=Join-Path $goaRoot "artifacts/unity/riposte-choice-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Real foreground mouse and keyboard with isolated saves';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

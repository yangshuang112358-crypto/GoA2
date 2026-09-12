param([ValidateRange(1152,3840)][int]$Width=1280,[ValidateRange(768,2160)][int]$Height=800)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
function Open-PresetMenu {
    Click '^调试$'
    Click -Element 'open-debug-presets'
}
function Confirm-Preset([string]$Phase) {
    $goaConfirmedAt=[DateTime]::UtcNow
    Click -Element 'debug-presets-confirm'
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 100
        try {
            $goaUi=Read-Ui
            $goaFresh=(Get-Item -LiteralPath (Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json')).LastWriteTimeUtc -ge $goaConfirmedAt
        } catch { continue }
        if ($goaFresh -and $goaUi.Phase -eq $Phase -and @($goaUi.Buttons | Where-Object Name -eq 'debug-presets-confirm').Count -eq 0) { return }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    throw "Preset did not finish loading into $Phase."
}
try {
    Close-QaPlayer
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 150
        try { $goaUi=Read-Ui } catch { continue }
        if ($goaUi.Phase -eq 'HeroSelection' -and $goaUi.Revision -eq 0) { break }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    Check ($goaUi.Phase -eq 'HeroSelection' -and $goaUi.Revision -eq 0) 'A fresh isolated Player is ready'
    Click '^调试$'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -eq 'open-debug-presets' -and $_.Enabled }).Count -eq 1) 'The game exposes a native debug-position launcher'
    Click '^自动选英雄与出生$'
    $goaBefore=Save-State
    $goaBeforeJson=Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Raw
    Click -Element 'open-debug-presets'
    $goaPresets=Get-Content -LiteralPath (Join-Path $goaRoot 'tools/debug-positions.json') -Raw | ConvertFrom-Json
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'debug-preset-*' -and $_.Enabled }).Count -eq $goaPresets.Count) 'All prepared positions are available inside the Player'
    Click -Element 'debug-preset-axe-reflection'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 4 | Out-Null
    Check ((Read-Ui).Seat -eq 0 -and (Read-Ui).Revision -eq $goaBefore.Revision) 'Selecting a preset and pressing a seat shortcut does not alter the current match behind the dialog'
    Click -Element 'debug-presets-cancel'
    $goaAfterCancel=Save-State
    Check ((Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Raw) -ceq $goaBeforeJson) 'Cancelling preserves the entire current match'

    Open-PresetMenu; Click -Element 'debug-preset-axe-reflection'; Confirm-Preset 'Action'
    $goaUi=Read-Ui
    Check ($goaUi.Phase -eq 'Action' -and $goaUi.Revision -eq 10 -and $goaUi.Seat -eq 0) 'The native launcher starts before the attack at the original attacker seat'
    Check ((Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Raw) -ceq $goaBeforeJson) 'Loading the new position first saves the previous match'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 4 | Out-Null
    $goaFourth=Read-Ui
    Check ($goaFourth.Seat -eq 3 -and @($goaFourth.Buttons | Where-Object { $_.Name -eq 'seat-4' -and $_.Visible }).Count -eq 1) 'The fourth selected character scrolls into view in the expanded roster'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    $goaFirst=Read-Ui
    $goaSameMap=$goaFirst.Zoom -eq $goaUi.Zoom -and $goaFirst.Focus.x -eq $goaUi.Focus.x -and $goaFirst.Focus.y -eq $goaUi.Focus.y
    Check ($goaFirst.Seat -eq 0 -and $goaSameMap -and @($goaFirst.Buttons | Where-Object { $_.Name -eq 'seat-1' -and $_.Visible }).Count -eq 1) 'Switching back reveals the first character without changing the map'
    Click -Element 'begin-primary';Click -Element 'optional-discard-gold'
    Click '^确认弃置 猛攻$'
    if((Read-Ui).TopExpanded) { Click -Element 'toggle-top' }
    Click -Element 'focus-hero';Select-Cell 6 -5
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Click '^反射屏障 · 抵挡$'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Click -Element 'forced-discard-silver'; Click '^确认弃置 铜墙铁壁$'
    $goaContinued=Save-State
    Check (@($goaContinued.Events | Where-Object Kind -eq 'AttackResolved').Count -eq 1 -and @($goaContinued.Events | Where-Object Kind -eq 'OptionalDiscardCompleted').Count -eq 1) 'The entire manual interaction completes each attack and cost once'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'native-preset-reflection' | Out-Null

    foreach ($goaPreset in $goaPresets | Where-Object id -ne 'axe-reflection') {
        Open-PresetMenu; Click -Element ('debug-preset-'+$goaPreset.id); Confirm-Preset $goaPreset.phase
        $goaState=Save-State; $goaUi=Read-Ui
        $goaPending=if ($null -eq $goaState.Pending) { '' } else { $goaState.Pending.Kind }
        Check ($goaUi.Phase -eq $goaPreset.phase -and $goaPending -eq $goaPreset.pending) ("Preset "+$goaPreset.id+" reaches its specified phase and choice")
        $goaChooser=if ($null -ne $goaState.Pending) { [int]$goaState.Pending.ChooserSeat } elseif ($null -ne $goaState.ActiveSeat) { [int]$goaState.ActiveSeat } elseif ($goaState.RoundEnd -and @($goaState.RoundEnd.Upgrades | Where-Object { $_.PendingLevels.Count -gt 0 }).Count) { [int](@($goaState.RoundEnd.Upgrades | Where-Object { $_.PendingLevels.Count -gt 0 })[0].Seat) } else { 0 }
        Check ($goaUi.Seat -eq $goaChooser) ("Preset "+$goaPreset.id+" selects the person who can continue")
    }
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'native-preset-spawn' | Out-Null

    $goaFormal=Get-Content -LiteralPath (Join-Path $goaRoot 'tests/scenarios/formal-turn.json') -Raw | ConvertFrom-Json
    $goaFormal.Id='ui-native-presets-formal'; $goaFormal.Steps=@($goaFormal.Steps | Select-Object -First 8)
    $goaFormalPath=Join-Path $goaRoot 'artifacts/unity/ui-native-presets-formal.json'
    $goaFormal | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $goaFormalPath -Encoding utf8
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaFormalPath
    $goaFormalRun=Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/scenarios/ui-native-presets-formal/headless') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    Open-Save (Join-Path $goaFormalRun.FullName 'report.json.save.json') 'Planning' 8
    Click '^调试$'
    $goaState=Save-State
    Check (-not $goaState.Sandbox -and -not $goaState.QuickSelection -and @((Read-Ui).Buttons | Where-Object Name -eq 'open-debug-presets').Count -eq 0) 'A formal confirmation match exposes no debug-position launcher and keeps formal selection'
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message})
    throw
} finally {
    $goaReport=Join-Path $goaRoot "artifacts/unity/presets-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Native preset menu operated with real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

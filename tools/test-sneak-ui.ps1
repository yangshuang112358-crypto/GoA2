param([ValidateRange(1280,3840)][int]$Width=1280,[ValidateRange(800,2160)][int]$Height=800)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
try {
    Open-Setup 'sneak-move' 7
    Click -Element 'begin-primary'
    if((Read-Ui).TopExpanded) { Click -Element 'toggle-top' }
    Click -Element 'focus-hero';Select-Cell 8 -10
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    Check ((Read-Ui).Revision -eq 9) 'Space confirms the adjacent attack'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Click '^不防御$'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object Name -eq 'effect-move-skip').Count -eq 0) 'The defender cannot submit the attacker movement choice'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    $goaPending=Save-State
    Check ($goaPending.Pending.Kind -eq 'effect_move' -and $goaPending.Revision -eq 10) 'The original attacker receives the optional movement after the defeat'
    $goaPath=Join-Path $goaRoot 'artifacts/unity/sneak-ui-pending.json'
    Copy-Item -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Destination $goaPath -Force
    Click -Element 'focus-hero';Select-Cell 8 -10;Click '^取消$'
    Check ((Save-State).Revision -eq 10) 'Cancelling a destination leaves the completed attack and movement pending'
    Select-Cell 8 -10
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'sneak-move-confirm' | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    $goaMoved=Save-State
    Check (@($goaMoved.Units | Where-Object { $_.Id -eq 'hero:0' -and $_.Position.X -eq 8 -and $_.Position.Y -eq -10 }).Count -eq 1) 'Space moves into the defeated hero cell'
    Check (@($goaMoved.Events | Where-Object Kind -eq 'UnitMoved').Count -eq 1 -and @($goaMoved.Events | Where-Object Kind -eq 'AttackResolved').Count -eq 1 -and $null -eq $goaMoved.Execution) 'The attack and card-text movement resolve once'
    Open-Save $goaPath 'EffectChoice' 10
    Click -Element 'effect-move-skip'
    $goaSkipped=Save-State
    Check (@($goaSkipped.Events | Where-Object Kind -eq 'UnitMoved').Count -eq 0 -and $null -eq $goaSkipped.Execution) 'Restored optional movement can be skipped without changing position'
    Click '^调试$';Click -Element 'open-debug-presets';Click -Element 'debug-preset-sneak-static';Click -Element 'debug-presets-confirm'
    Start-Sleep -Milliseconds 500
    $goaStatic=Save-State
    Check ($goaStatic.Pending.Kind -eq 'effect_move' -and $goaStatic.Pending.ChooserSeat -eq 1) 'The static-field interaction preset opens at the correct choice'
    Check (@((Read-Ui).Cells | Where-Object { $_.X -eq 8 -and $_.Y -eq -8 -and $_.Legal }).Count -eq 0) 'The outside cell is not highlighted as a legal card-text move'
    Click -Element 'effect-move-skip'
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message});throw
} finally {
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Real foreground mouse and keyboard with isolated saves';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaRoot "artifacts/unity/sneak-ui-${Width}x${Height}.json") -Encoding utf8
}

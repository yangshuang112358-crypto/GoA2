param([ValidateRange(1152,3840)][int]$Width=1600, [ValidateRange(768,2160)][int]$Height=1000)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[System.Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
function Attack-And-Defend([string]$CardName) {
    Click -Element 'begin-primary'
    Click -Element 'focus-hero'
    Select-Cell 8 -9; Click '^确认攻击 '
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Click -Element 'hand-green'; Click "^确认使用 $CardName 防御$"
}

try {
    Open-Setup 'deflection-barrier' 8
    Attack-And-Defend '偏转屏障'
    $goaState=Save-State
    Check ($goaState.Pending.Kind -eq 'forced_discard' -and $goaState.Pending.ChooserSeat -eq 0 -and $goaState.Execution.DefenseResponse.ControllerSeat -eq 1) 'Successful barrier keeps the parent attack and waits for its attacker'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'forced-discard-*' }).Count -eq 0) 'The defender cannot access attacker discard choices'
    $goaPendingPath=Join-Path $goaRoot 'artifacts/unity/barrier-ui-pending.json'
    Copy-Item -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Destination $goaPendingPath -Force
    $goaPendingRevision=$goaState.Revision
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'forced-discard-*' -and $_.Enabled }).Count -eq 4) 'The attacker sees exactly its four legal hand choices'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'barrier-ui-pending' | Out-Null
    Click -Element 'hand-gold'; Click '^取消$'
    $goaState=Save-State
    Check ($goaState.Revision -eq $goaPendingRevision -and $goaState.Pending.Kind -eq 'forced_discard') 'Cancel only clears the local selection and cannot skip forced discard'
    Click -Element 'hand-gold'; Click '^确认弃置 近身射击$'
    $goaState=Save-State
    Check ($null -eq $goaState.Execution -and $goaState.ActiveSeat -eq 1 -and @($goaState.Players[0].Cards | Where-Object { $_.CardId -eq 'sabina-00-近身射击' -and $_.Zone -eq 4 }).Count -eq 1) 'Confirming the hand card discards it and resumes the next hero'
    Check ((Read-Ui).DiscardDotCount -eq 2 -and $goaState.Effects.Count -eq 0) 'Two actual discard dots appear without creating reflection immunity'
    Open-Save $goaPendingPath 'EffectChoice' $goaPendingRevision
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'forced-discard-*' -and $_.Enabled }).Count -eq 4) 'A restored counter again exposes only the correct attacker choices'
    Click -Element 'forced-discard-gold'; Click '^确认弃置 近身射击$'
    $goaState=Save-State
    Check (@($goaState.Events | Where-Object Kind -eq 'AttackResolved').Count -eq 1 -and @($goaState.Events | Where-Object Kind -eq 'ForcedDiscardCompleted').Count -eq 1) 'Restoring and finishing does not repeat the attack or counter'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'barrier-ui-resumed' | Out-Null

    Open-Setup 'reflection-barrier' 12
    Attack-And-Defend '反射屏障'
    Check ((Read-Ui).ActiveEffectCount -eq 0) 'Reflection immunity waits until the forced discard has completed'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Click -Element 'hand-gold'; Click '^确认弃置 近身射击$'
    $goaState=Save-State
    Check ($goaState.Effects.Count -eq 1 -and $goaState.Effects[0].SourceCardId -eq 'wasp-10-反射屏障' -and $goaState.Effects[0].ProtectedUnitId -eq 'hero:1') 'The completed counter creates sourced protection on Wasp'
    Check (@((Read-Ui).Buttons | Where-Object { $_.Name -like 'effect-area-*' }).Count -eq 0) 'Self protection does not display a misleading area circle'
    Pass-Seat 2
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 3 | Out-Null
    Click -Element 'begin-primary'
    Click -Element 'focus-hero'
    $goaUi=Read-Ui
    $goaProtected=$goaUi.Cells | Where-Object { $_.X -eq 8 -and $_.Y -eq -9 }
    $goaOther=$goaUi.Cells | Where-Object { $_.X -eq 4 -and $_.Y -eq -8 }
    Check (-not $goaProtected.Legal -and $goaOther.Legal) 'Second ranged attacker sees the other enemy highlighted while Wasp is protected'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'barrier-ui-protected' | Out-Null
    Select-Cell 4 -8; Click '^确认攻击 '
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 4 | Out-Null
    Click -Element 'hand-blue'; Click '^确认使用 挑战者 防御$'
    Pass-Seat 4
    $goaState=Save-State
    Check ($goaState.Turn -eq 2 -and $goaState.Effects.Count -eq 0 -and $goaState.RedCrystal -eq 7) 'Protection expires after remaining heroes finish without crystal damage'
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaReport=Join-Path $goaRoot "artifacts/unity/barriers-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified scenarios followed by real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

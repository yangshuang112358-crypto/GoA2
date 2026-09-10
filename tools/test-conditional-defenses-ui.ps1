param(
    [ValidateRange(1152,3840)][int]$Width=1600,
    [ValidateRange(768,2160)][int]$Height=1000,
    [ValidateSet('all','melee-block','riposte-hand','riposte-empty','riposte-victory','lead-charge','lead-charge-unavailable','riposte-ranged')][string]$Case='all'
)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[System.Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
function Begin-AdjacentAttack {
    Click -Element 'begin-primary'; Click -Element 'focus-hero'; Select-Cell 8 -10; Click '^确认攻击 '
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
}
function Use-BlueDefense([string]$Name) { Click -Element 'hand-blue'; Click "^确认使用 $Name 防御$" }
try {
    foreach($goaCase in @(@('melee-block','近身格挡'),@('riposte-hand','近身还击'))) {
        if($Case -ne 'all' -and $Case -ne $goaCase[0]) { continue }
        Open-Setup $goaCase[0] 8
        Begin-AdjacentAttack
        Check (@((Read-Ui).Buttons | Where-Object { $_.Text -eq ($goaCase[1]+' · 抵挡') }).Count -eq 1) "$($goaCase[0]): non-ranged block is available"
        Use-BlueDefense $goaCase[1]
        $goaState=Save-State
        Check ($goaState.Pending.Kind -eq 'forced_discard' -and $goaState.Pending.ChooserSeat -eq 0) "$($goaCase[0]): the original attacker must choose its own discard"
        Check ($goaState.Effects.Count -eq 1 -and $goaState.Effects[0].SourceCardId -eq 'wasp-00-闪耀之刃') "$($goaCase[0]): original attack text has already resolved"
        & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
        Click -Element 'hand-red'; Click '^确认弃置 电击$'
        $goaState=Save-State
        Check ($goaState.BlueCrystal -eq 7 -and $goaState.RedCrystal -eq 7 -and $goaState.ActiveSeat -eq 2 -and $null -eq $goaState.Execution) "$($goaCase[0]): choosing a hand card preserves both heroes and resumes play"
    }
    if($Case -in @('all','riposte-empty')) {
        Open-Setup 'riposte-empty' 12
        Begin-AdjacentAttack; Use-BlueDefense '近身还击'
        $goaState=Save-State
        Check ($goaState.BlueCrystal -eq 6 -and $goaState.Players[0].AwaitingRespawn -and $goaState.Players[1].Gold -eq 1 -and $goaState.Players[3].Gold -eq 1) 'Empty-hand riposte defeats the attacker and awards the correct team'
        Check ($null -eq $goaState.Execution -and $goaState.ActiveSeat -eq 2 -and @($goaState.Events | Where-Object Kind -eq 'AttackResolved').Count -eq 1) 'The counter finishes its parent attack exactly once'
        Check (@($goaState.Events | Where-Object { $_.Kind -eq 'HeroDefeatSource' -and $_.PrivateTo -eq 1 -and $_.CardId -eq 'tigerclaw-17-近身还击' }).Count -eq 1) 'Counter defeat records a private defense-card source'
        & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'riposte-ui-defeat' | Out-Null
    }
    if($Case -in @('all','riposte-victory')) {
        Open-Setup 'riposte-victory' 13
        Begin-AdjacentAttack; Use-BlueDefense '近身还击'
        $goaState=Save-State
        Check ((Read-Ui).Phase -eq 'Finished' -and $goaState.Winner -eq 1 -and $goaState.BlueCrystal -eq 0) 'Counter crystal damage immediately produces Red victory'
        Check ($null -eq $goaState.ActiveSeat -and $null -eq $goaState.Pending -and $goaState.Events[-1].Kind -eq 'MatchWon') 'Finished match opens no extra action or respawn prompt'
        Check (@((Read-Ui).Labels | Where-Object { $_.Name -eq 'match-stage' -and $_.Text -eq '红队获胜' -and $_.Visible }).Count -eq 1) 'The fixed header keeps the winner visible after sidebar scrolling'
        & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'riposte-ui-victory' | Out-Null
    }
    if($Case -in @('all','lead-charge')) {
        Open-Setup 'lead-charge' 9
        Begin-AdjacentAttack
        Check (@((Read-Ui).Buttons | Where-Object { $_.Text -eq '带头冲锋 · 抵挡' }).Count -eq 1) 'Adjacent friendly minion enables Lead Charge'
        Click -Element 'hand-green'; Click '^确认使用 带头冲锋 防御$'
        $goaState=Save-State
        Check ($goaState.RedCrystal -eq 7 -and $goaState.ActiveSeat -eq 1 -and @($goaState.Events | Where-Object Kind -eq 'ForcedDiscardRequired').Count -eq 0) 'Lead Charge blocks without an extra counter choice'
    }
    if($Case -in @('all','lead-charge-unavailable')) {
        Open-Setup 'lead-charge-unavailable' 8
        Begin-AdjacentAttack
        Check (@((Read-Ui).Labels | Where-Object { $_.Name -eq 'defense-restriction-green' -and $_.Text -eq '带头冲锋：没有相邻友方小兵' }).Count -eq 1) 'Unavailable Lead Charge explains the missing friendly minion'
        Check (@((Read-Ui).Buttons | Where-Object { $_.Text -eq '带头冲锋 · 抵挡' }).Count -eq 0) 'Unavailable defense is absent from legal options'
        & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
        Check (@((Read-Ui).Labels | Where-Object { $_.Name -like 'defense-restriction-*' }).Count -eq 0) 'Another seat does not see private defense-card restrictions'
        & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
        & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'lead-charge-ui-restriction' | Out-Null
    }
    if($Case -in @('all','riposte-ranged')) {
        Open-Setup 'riposte-ranged' 9
        Begin-AdjacentAttack
        Check (@((Read-Ui).Labels | Where-Object { $_.Name -eq 'defense-restriction-blue' -and $_.Text -eq '近身还击：只可抵挡非远程攻击' }).Count -eq 1) 'Adjacent ranged attack still explains the non-ranged restriction'
        Check (@((Read-Ui).Buttons | Where-Object { $_.Text -eq '近身还击 · 抵挡' }).Count -eq 0) 'Riposte cannot be confirmed against a ranged attack'
        & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'riposte-ui-ranged-restriction' | Out-Null
        Click -Element 'hand-red'; Click '^确认使用 偷袭 防御$'
        $goaState=Save-State
        Check ($goaState.RedCrystal -eq 6 -and $goaState.ActiveSeat -eq 2) 'Insufficient numerical defense remains selectable and fails normally'
    }
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaSuffix=if($Case -eq 'all') { '' } else { '-'+$Case }
    $goaReport=Join-Path $goaRoot "artifacts/unity/conditional-defenses-ui-${Width}x${Height}${goaSuffix}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified scenarios followed by real foreground mouse and keyboard';selectedCase=$Case;checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

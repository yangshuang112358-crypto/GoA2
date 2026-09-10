param([ValidateRange(1152,3840)][int]$Width=1600, [ValidateRange(768,2160)][int]$Height=1000)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[System.Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
try {
    foreach($goaCase in @(@('cleave',4,1),@('deadly-sweep',3,2),@('death-spin',2,3))) {
        Open-Setup $goaCase[0] 13
        Click -Element 'begin-primary'; Click -Element 'focus-hero'
        Check (-not ((Read-Ui).Cells | Where-Object { $_.X -eq 6 -and $_.Y -eq -7 }).Legal) "$($goaCase[0]): protected heavy minion is not a legal attack target"
        Select-Cell 7 -8; Click '^确认攻击 '
        & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
        $goaState=Save-State
        Check ($goaState.Execution.Attack.CardTextBonus -eq 5*$goaCase[2] -and $goaState.Execution.Attack.CardTextSourceUnits.Count -eq 5 -and $goaState.Execution.Attack.CardTextSourceUnits -contains 'minion:-3,0') "$($goaCase[0]): all five adjacent enemies count, including the protected heavy"
        Check ((Read-Ui).AttackSourceSummary -eq "攻击者相邻敌方：5个 × $($goaCase[2]) = +$(5*$goaCase[2])") "$($goaCase[0]): visible rule summary explains the actual count and factor"
        Check ($goaState.Execution.Attack.EnemySupport -eq 1 -and $goaState.Execution.Attack.FinalAttack -eq $goaCase[1]+5*$goaCase[2]+1) "$($goaCase[0]): minion support remains separate from card text"
        & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name ($goaCase[0]+'-ui-sources') | Out-Null
        Click -Element 'hand-blue'; Click '^确认使用 挑战者 防御$'
        $goaState=Save-State
        Check ($goaState.RedCrystal -eq 6 -and $goaState.Players[1].AwaitingRespawn -and $goaState.Players[0].Gold -eq 1 -and $goaState.ActiveSeat -eq 2) "$($goaCase[0]): ignoring minion support does not ignore the card bonus and the attack finishes"
    }
    Open-Setup 'backstab' 10
    Click -Element 'begin-primary'; Click -Element 'focus-hero'; Select-Cell 7 -8; Click '^确认攻击 '
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    $goaState=Save-State
    Check ($goaState.Execution.Attack.CardTextBonus -eq 2 -and $goaState.Execution.Attack.CardTextSourceUnits.Count -eq 2 -and $goaState.Execution.Attack.CardTextSourceUnits -notcontains 'hero:0') 'Backstab counts two other friendly supporters but grants two only once'
    Check ((Read-Ui).AttackSourceSummary -eq '目标相邻的其他友方：2个，牌文 +2') 'Backstab summary identifies support around the target'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'backstab-ui-sources' | Out-Null
    Click -Element 'hand-blue'; Click '^确认使用 挑战者 防御$'
    $goaState=Save-State
    Check ($goaState.RedCrystal -eq 6 -and $goaState.ActiveSeat -eq 2 -and @($goaState.Events | Where-Object Kind -eq 'AttackResolved').Count -eq 1) 'Backstab resumes the next action after one attack resolution'
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaReport=Join-Path $goaRoot "artifacts/unity/adjacent-attacks-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified scenarios followed by real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

param([ValidateRange(1152,3840)][int]$Width=1600, [ValidateRange(768,2160)][int]$Height=1000)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[System.Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
try {
    Open-Setup 'marksman' 8
    Click -Element 'begin-primary'; Click -Element 'focus-hero'; Select-Cell 8 -9; Click '^确认攻击 '
    $goaState=Save-State
    Check ($goaState.Execution.Attack.BaseAttack -eq 4 -and $goaState.Execution.Attack.CardTextBonus -eq 2 -and $goaState.Execution.Attack.FinalAttack -eq 6) 'Marksman adds two for the revealed unresolved attack'
    Check (@($goaState.Players[1].Cards | Where-Object { $_.CardId -eq 'wasp-01-电击' -and $_.Zone -eq 2 }).Count -eq 1) 'The defender attack has been revealed without being resolved'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'marksman-ui-bonus' | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object { $_.Text -eq '控物 · 防御 5 < 6' }).Count -eq 1) 'Defense choice shows the actual five-versus-six comparison'
    Click -Element 'hand-blue'; Click '^确认使用 控物 防御$'
    $goaState=Save-State
    Check ($goaState.RedCrystal -eq 6 -and $goaState.Players[1].AwaitingRespawn -and $goaState.Players[0].Gold -eq 1) 'The numeric defense fails against six and pays defeat rewards'

    Open-Setup 'headshot' 9
    Click -Element 'begin-primary'; Click -Element 'focus-hero'
    Check (@((Read-Ui).Cells | Where-Object { $_.X -eq 8 -and $_.Y -eq -10 -and $_.Legal }).Count -eq 1) 'Headshot can choose the adjacent enemy'
    Select-Cell 8 -10; Click '^确认攻击 '
    $goaState=Save-State
    Check ($goaState.Execution.Attack.Ranged -and $goaState.Execution.Attack.CardTextBonus -eq 2 -and @($goaState.Players[1].Cards | Where-Object { $_.CardId -eq 'wasp-00-闪耀之刃' -and $_.Zone -eq 3 }).Count -eq 1) 'A passed basic attack still counts while the adjacent attack remains ranged'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object { $_.Text -match '^抵挡屏障 ·' }).Count -eq 0) 'Non-adjacent barrier is excluded from this adjacent defense'
    Click -Element 'hand-red'; Click '^确认使用 电击 防御$'
    $goaState=Save-State
    Check ($goaState.RedCrystal -eq 7 -and -not $goaState.Players[1].AwaitingRespawn -and $goaState.ActiveSeat -eq 2) 'Six defense succeeds and the game continues'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'headshot-ui-defense' | Out-Null
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message}); throw
} finally {
    $goaReport=Join-Path $goaRoot "artifacts/unity/marksman-ui-${Width}x${Height}.json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Verified scenarios followed by real foreground mouse and keyboard';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "UI report: $goaReport"
}

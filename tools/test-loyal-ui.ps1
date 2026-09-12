param([ValidateRange(1280,3840)][int]$Width=1280,[ValidateRange(800,2160)][int]$Height=800)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
try {
    Open-Setup 'loyal-recover' 10
    Click -Element 'begin-primary'
    $goaPending=Save-State
    Check ($goaPending.Pending.Kind -eq 'recover_discard' -and $goaPending.Revision -eq 11) 'The skill offers recovery when adjacent to a minion'
    $goaPath=Join-Path $goaRoot 'artifacts/unity/loyal-ui-pending.json'
    Copy-Item -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Destination $goaPath -Force
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 2 | Out-Null
    Check (@((Read-Ui).Buttons | Where-Object Name -like 'recover-card-*').Count -eq 0) 'Other seats cannot see the private recovery options'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Click -Element 'recover-card-red';Click '^取消$'
    Check ((Save-State).Revision -eq 11) 'Cancel only clears the local recovery preview'
    Click -Element 'recover-card-red'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    $goaRecovered=Save-State
    Check (@($goaRecovered.Players[0].Cards | Where-Object { $_.CardId -eq 'shargatha-01-劈砍' -and $_.Zone -eq 0 }).Count -eq 1) 'Space returns the chosen card to the hand'
    Open-Save $goaPath 'EffectChoice' 11
    Click -Element 'recover-card-skip'
    $goaSkipped=Save-State
    Check (@($goaSkipped.Players[0].Cards | Where-Object Zone -eq 4).Count -eq 1 -and $null -eq $goaSkipped.Execution) 'Restored recovery can be skipped without changing the discard pile'
    Click '^调试$';Click -Element 'open-debug-presets';Click -Element 'debug-preset-loyal-defense';Click -Element 'debug-presets-confirm'
    Start-Sleep -Milliseconds 500
    Click '^劈砍 · 防御'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Space | Out-Null
    $goaDefended=Save-State
    Check (-not $goaDefended.Players[0].AwaitingRespawn -and $goaDefended.BlueCrystal -eq 7) 'The just-recovered card can defend against Draw'
} catch {
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message});throw
} finally {
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');method='Real foreground mouse and keyboard with isolated saves';checks=$goaChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaRoot "artifacts/unity/loyal-ui-${Width}x${Height}.json") -Encoding utf8
}

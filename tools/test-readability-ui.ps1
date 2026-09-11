param([ValidateRange(1280,3840)][int]$Width=1600,[ValidateRange(800,2160)][int]$Height=1000)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
function Key([string]$Value) { & "$PSScriptRoot/qa-player.ps1" -Action Key -Key $Value | Out-Null }
function Capture([string]$Name) { & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name ('batch03-'+$Name) | Out-Null }
function Target-Unit($Unit) { Key 'Home';Select-Cell $Unit.Position.X $Unit.Position.Y;Key 'Space' }
try {
    Close-QaPlayer
    $goaSetup=[ordered]@{SchemaVersion=1;Id='readability-setup';Name='大字号与紫卡升级界面验收';Steps=@(
        @{Command='DebugPrepare';Value='wasp,shargatha,brogan,arien'},
        @{Command='DebugSetGold';Value='28';Target='p1'},
        @{Command='DebugAdvance';Value='round';Expect=@{Phase='RoundEnd'}}
    )}
    $goaSetupPath=Join-Path $goaRoot 'artifacts/unity/readability-setup.json'
    $goaSetup | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $goaSetupPath -Encoding utf8
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaSetupPath
    $goaRun=Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/scenarios/readability-setup/headless') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    Open-Save (Join-Path $goaRun.FullName 'report.json.save.json') 'RoundEnd' 3
    $goaBefore=Save-State
    $goaCatalog=(Get-Content -LiteralPath (Join-Path $goaRoot 'content/canonical/cards.json') -Raw | ConvertFrom-Json).cards
    $goaRevealed=@($goaBefore.Events | Where-Object Kind -eq 'CardRevealed' | Select-Object -Last 4)
    $goaTiles=@((Read-Ui).Elements | Where-Object Name -match '^revealed-seat-[1-4]$' | Sort-Object {$_.Bounds.x})
    $goaValues=@($goaTiles | ForEach-Object { $goaSeat=[int]$_.Name.Split('-')[-1]-1;$goaCardId=($goaRevealed | Where-Object Seat -eq $goaSeat).CardId;[int]($goaCatalog | Where-Object id -eq $goaCardId).initiative })
    Check ($goaValues.Count -eq 4 -and ($goaValues -join ',') -eq (($goaValues | Sort-Object -Descending) -join ',')) 'Revealed cards appear from highest initiative to lowest'
    Check (@((Read-Ui).Elements | Where-Object { $_.Name -match '^revealed-color-[1-4]$' -and $_.Bounds.height -eq 9 }).Count -eq 4) 'All revealed card color stripes are nine pixels'
    $goaTeams=@((Read-Ui).Elements | Where-Object Name -match '^revealed-team-[1-4]$')
    Check (@($goaTeams | Where-Object {$_.Bounds.height -eq 6}).Count -eq 4 -and @($goaTeams | Where-Object {$_.Name -match '[13]$' -and $_.Background.b -gt $_.Background.r}).Count -eq 2 -and @($goaTeams | Where-Object {$_.Name -match '[24]$' -and $_.Background.r -gt $_.Background.b}).Count -eq 2) 'Revealed cards have separate six-pixel blue and red team stripes'
    foreach($goaTeam in $goaTeams) {
        $goaSeat=$goaTeam.Name.Split('-')[-1]
        $goaTile=$goaTiles | Where-Object Name -eq ('revealed-seat-'+$goaSeat)
        $goaColor=(Read-Ui).Elements | Where-Object Name -eq ('revealed-color-'+$goaSeat)
        Check ($goaTeam.Bounds.y -gt ($goaColor.Bounds.y+$goaColor.Bounds.height) -and [Math]::Abs(($goaTeam.Bounds.y+$goaTeam.Bounds.height)-($goaTile.Bounds.y+$goaTile.Bounds.height)) -le 2) "Seat $goaSeat team stripe is at the bottom, separate from its top card stripe"
    }
    Click -Element 'resolve-round-end'
    $goaUi=Read-Ui
    Check (@($goaUi.Buttons | Where-Object Name -like 'upgrade-card-*').Count -eq 6) 'All six candidates are present together'
    $goaRows=@($goaUi.Elements | Where-Object Name -like 'upgrade-row-*')
    Check ($goaRows.Count -eq 3 -and $goaRows[0].Bounds.y -lt $goaRows[1].Bounds.y -and $goaRows[1].Bounds.y -lt $goaRows[2].Bounds.y) 'Three colors occupy three ordered rows'
    foreach($goaRow in $goaRows) {
        $goaPair=@($goaUi.Buttons | Where-Object { $_.Name -like 'upgrade-card-*' -and $_.Bounds.y -ge $goaRow.Bounds.y -and $_.Bounds.y -lt ($goaRow.Bounds.y+$goaRow.Bounds.height) })
        Check ($goaPair.Count -eq 2 -and [Math]::Abs($goaPair[0].Bounds.y-$goaPair[1].Bounds.y) -lt 1) "Two cards share $($goaRow.Name)"
    }
    Check ($goaUi.MinimumFontSize -ge 26) 'Visible text uses at least 26 pixels'
    if($Width -ge 1600 -and $Height -ge 1000) { Check (@($goaUi.Buttons | Where-Object { $_.Name -like 'upgrade-card-*' -and $_.Visible }).Count -eq 6) 'All six candidate centers fit the large window without scrolling' }
    Capture 'six-upgrades'
    $goaCards=@('wasp-03-电能波','wasp-08-偏转屏障','wasp-15-引力控制','wasp-05-电能爆炸','wasp-10-反射屏障','wasp-17-意念黑洞')
    for($goaIndex=0;$goaIndex -lt $goaCards.Count;$goaIndex++) {
        Click -Element ('upgrade-card-'+$goaCards[$goaIndex]);$goaRevision=(Read-Ui).Revision
        Key 'Space'
        Check ((Read-Ui).Revision -eq $goaRevision+1) "Space confirms upgrade $($goaIndex+1) exactly once"
        if($goaIndex -eq 0) {
            Check (@((Read-Ui).Buttons | Where-Object { $_.Name -in @('upgrade-card-wasp-02-回旋镖','upgrade-card-wasp-03-电能波') -and -not $_.Enabled }).Count -eq 2) 'Used color remains listed but disabled'
        }
    }
    $goaState=Save-State
    Check ($null -eq $goaState.Players[0].PurpleCardId -and (Read-Ui).Phase -eq 'RoundEnd') 'Level eight pauses before awarding purple'
    Check (@((Read-Ui).Buttons | Where-Object Name -like 'upgrade-card-*').Count -eq 1) 'The final upgrade shows the unique purple candidate'
    Capture 'purple-choice'
    Click '^读取$'
    Click -Element 'upgrade-card-wasp-12-电闪雷鸣';Key 'Space'
    $goaState=Save-State
    Check ($goaState.Round -eq 2 -and $goaState.Players[0].PurpleCardId -eq 'wasp-12-电闪雷鸣' -and $goaState.Players[0].Cards.Count -eq 5 -and $goaState.Players[0].UpgradeHistory.Count -eq 6) 'Purple confirmation preserves five cards and six passive sources'
    $goaDot=(Read-Ui).Elements | Where-Object Name -eq 'purple-dot-1'
    Check ($goaDot.Visible -and $goaDot.Bounds.width -gt 30 -and $goaDot.Background.b -gt $goaDot.Background.g) 'A larger permanent purple circle appears beside the four slots'
    & "$PSScriptRoot/qa-player.ps1" -Action Hover -X ([int]($goaDot.Bounds.x+15)) -Y ([int]($goaDot.Bounds.y+15)) | Out-Null
    Check (@((Read-Ui).Elements | Where-Object Name -eq 'purple-preview').Count -eq 1) 'Hover opens the ultimate card information'
    Check (((Read-Ui).Labels | Where-Object Name -eq 'purple-preview-text').Text -eq ($goaCatalog | Where-Object id -eq 'wasp-12-电闪雷鸣').primary_action.text) 'Hover exposes the complete formal ultimate text'
    Capture 'purple-hover'
    Click -Element 'hand-gold'
    $goaBonus=@((Read-Ui).Labels | Where-Object Name -like 'hand-gold-*-bonus')
    Check ($goaBonus.Count -eq 4 -and @($goaBonus | Where-Object { $_.Text -eq '+1' -and $_.Color.g -gt $_.Color.r }).Count -eq 4) 'Selected card shows four applicable green +1 values'
    Check (((Read-Ui).Elements | Where-Object Name -eq 'hand-gold').BorderTop -eq 9) 'Hand card color stripe is nine pixels'
    Capture 'green-bonuses'
    Click -Element 'hand-blue'
    Check (((Save-State).Players[0].Cards | Where-Object CardId -eq 'wasp-17-意念黑洞').Zone -eq 1) 'Dragging the horizontal scrollbar makes the fifth hand card selectable'
    Click -Element 'history-open'
    Check (((Read-Ui).Labels | Where-Object Name -eq 'history-page').Text -match '第 2 轮.*2 / 2') 'Expanded log opens the current round page'
    Click -Element 'history-previous'
    Check (@((Read-Ui).Labels | Where-Object Name -like 'history-event-*').Count -gt 12) 'Previous round exposes all records beyond the recent twelve'
    Capture 'round-history';Click -Element 'history-close'
    Click '^图鉴 108$'
    Check (@((Read-Ui).Elements | Where-Object Name -like 'gallery-row-*').Count -eq 6) 'Gallery has six horizontal color rows'
    Click -Element 'gallery-all'
    Check (((Read-Ui).Labels | Where-Object Name -eq 'gallery-count').Text -match '108 / 108') 'Color rows retain all 108 cards'
    $goaRevision=(Read-Ui).Revision;Key 'Space'
    Check ((Read-Ui).Revision -eq $goaRevision) 'Space cannot submit a hidden confirmation under the gallery'
    Capture 'gallery-rows';Click -Element 'gallery-return'
    Click '^调试$'
    $goaState=Save-State
    Click -Element 'debug-attack'
    $goaMinion=$goaState.Units | Where-Object { $_.Team -eq 1 -and $_.Kind -eq 'melee' } | Select-Object -First 1
    Target-Unit $goaMinion
    $goaAfter=Save-State
    Check (@($goaAfter.Units | Where-Object Id -eq $goaMinion.Id).Count -eq 0 -and $goaAfter.Players[0].Gold -gt $goaState.Players[0].Gold) 'Map attack removes a legal minion and awards gold'
    Click '^调试$';Click -Element 'debug-attack'
    Target-Unit ($goaAfter.Units | Where-Object Id -eq 'hero:1')
    Check ((Read-Ui).Phase -eq 'EffectChoice') 'Map attack on a hero opens normal defense'
    Key '2';Click -Element 'decline-defense';Key 'Space'
    $goaAfter=Save-State
    Check ($goaAfter.Players[1].AwaitingRespawn -and $goaAfter.RedCrystal -eq 6 -and $goaAfter.Turn -eq 1 -and $goaAfter.Round -eq 2) 'Declining defense applies hero defeat and returns without consuming the turn'
    Capture 'debug-attack'
} catch { $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message});throw }
finally {
    $goaReport=Join-Path $goaRoot "artifacts/unity/readability-ui-${Width}x${Height}-$($goaStarted.ToString('yyyyMMdd-HHmmss')).json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');width=$Width;height=$Height;checks=$goaChecks} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "Readability UI report: $goaReport"
}

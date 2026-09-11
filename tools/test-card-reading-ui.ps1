param([ValidateRange(1280,3840)][int]$Width=1600,[ValidateRange(800,2160)][int]$Height=1000)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
. "$PSScriptRoot/ui-qa-common.ps1"
function Hover-Name([string]$Name) {
    $goaUi=Read-Ui
    $goaElement=@($goaUi.Elements | Where-Object Name -eq $Name)[0]
    if (-not $goaElement) { $goaElement=@($goaUi.Buttons | Where-Object Name -eq $Name)[0] }
    if (-not $goaElement) { throw "Missing hover source $Name" }
    & "$PSScriptRoot/qa-player.ps1" -Action Hover -X ([int]($goaElement.Bounds.x+35)) -Y ([int]($goaElement.Bounds.y+35)) | Out-Null
    Start-Sleep -Milliseconds 400
}
function Check-Preview([string]$CardId) {
    $goaUi=Read-Ui
    $goaText=@($goaUi.Labels | Where-Object Name -eq 'card-preview-rules')[0]
    $goaExpected=($goaCatalog | Where-Object id -eq $CardId).primary_action.text
    Check ($null -ne $goaText -and $goaText.Text -ceq $goaExpected -and $goaText.Visible) "Full original rules are visible for $CardId"
    $goaBox=@($goaUi.Elements | Where-Object Name -eq 'card-preview')[0]
    Check ($goaBox.Bounds.x -ge 0 -and $goaBox.Bounds.y -ge 0 -and ($goaBox.Bounds.x+$goaBox.Bounds.width) -le $Width -and ($goaBox.Bounds.y+$goaBox.Bounds.height) -le $Height -and $goaText.Bounds.y -ge $goaBox.Bounds.y -and ($goaText.Bounds.y+$goaText.Bounds.height) -le ($goaBox.Bounds.y+$goaBox.Bounds.height)) "Full reading card fits $Width x $Height without scrolling"
    Check ($goaUi.CardPreviewScrollCount -eq 0 -and $goaText.FontSize -ge 26) 'Reading card has no inner scroll and keeps large type'
}
try {
    Close-QaPlayer
    $goaCatalog=(Get-Content -LiteralPath (Join-Path $goaRoot 'content/canonical/cards.json') -Raw | ConvertFrom-Json).cards
    $goaSetup=@{SchemaVersion=1;Id='card-reading-setup';Name='卡牌完整阅读验收';Steps=@(@{Command='DebugPrepare';Value='wasp,shargatha,brogan,arien'})}
    $goaPath=Join-Path $goaRoot 'artifacts/unity/card-reading-setup.json'
    $goaSetup | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $goaPath -Encoding utf8
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaPath
    $goaRun=Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/scenarios/card-reading-setup/headless') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    Open-Save (Join-Path $goaRun.FullName 'report.json.save.json') 'Planning' 1
    Check (@((Read-Ui).Labels | Where-Object Name -like 'hand-rules-*').Count -eq 5) 'Every hand card contains an inline rules preview'
    # Small windows may require the outer workspace to scroll; card rules themselves never scroll.
    Click -Element 'hand-gold'
    Hover-Name 'hand-gold';Check-Preview 'wasp-00-闪耀之刃'
    Check ((Read-Ui).Revision -eq 2) 'Hovering a selected hand card sends no game command'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name "card-reading-hand-${Width}x${Height}" | Out-Null
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key Escape | Out-Null
    Check (@((Read-Ui).Elements | Where-Object Name -eq 'card-preview').Count -eq 0) 'Escape dismisses the reading card'
    Check ((Read-Ui).NestedCardScrollCount -eq 0) 'Sidebar card details no longer nest a scroll box'
    Click '^图鉴 108$'
    Click -Element 'gallery-query'
    & "$PSScriptRoot/qa-text.ps1" -Text '电能波' | Out-Null
    Hover-Name 'inspect-gallery-wasp-03-电能波';Check-Preview 'wasp-03-电能波'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name "card-reading-long-${Width}x${Height}" | Out-Null
    Click -Element 'gallery-return'
    Check (@((Read-Ui).Elements | Where-Object Name -eq 'card-preview').Count -eq 0) 'Closing a view removes its old card preview'
    # Use a fresh setup through legal commands for an actual public revealed card.
    Click '^调试$';Click '^快速到轮末$'
    Hover-Name 'revealed-seat-1';Check-Preview 'wasp-07-抵挡屏障'
    Check (@((Read-Ui).Labels | Where-Object Name -like 'revealed-rules-*').Count -eq 4) 'All four revealed cards contain descriptions'
    Check ((Read-Ui).NestedCardScrollCount -eq 0) 'Revealed cards have no internal rules scroll box'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name "card-reading-revealed-${Width}x${Height}" | Out-Null
} catch { $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$_.Exception.Message});throw }
finally {
    $goaReport=Join-Path $goaRoot "artifacts/unity/card-reading-ui-${Width}x${Height}-$($goaStarted.ToString('yyyyMMdd-HHmmss')).json"
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');width=$Width;height=$Height;checks=$goaChecks} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $goaReport -Encoding utf8
    Write-Output "Card reading UI report: $goaReport"
}

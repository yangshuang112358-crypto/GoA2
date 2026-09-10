param(
    [ValidateRange(1152,3840)][int]$Width = 1600,
    [ValidateRange(768,2160)][int]$Height = 1000
)
$ErrorActionPreference = 'Stop'
$goaRoot = Split-Path -Parent $PSScriptRoot
$goaLayoutPath = Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json'
$goaReportPath = Join-Path $goaRoot "artifacts/unity/ui-smoke-${Width}x${Height}.json"
$goaChecks = [System.Collections.Generic.List[object]]::new()
$goaStarted = [DateTime]::UtcNow
function Read-Ui {
    $goaDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        try { return Get-Content -LiteralPath $goaLayoutPath -Raw | ConvertFrom-Json } catch { Start-Sleep -Milliseconds 100 }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    throw 'Player did not publish a readable UI snapshot.'
}
function Assert-Ui([bool]$Condition, [string]$Description) {
    $goaChecks.Add([pscustomobject]@{ check=$Description; passed=$Condition })
    if (-not $Condition) { throw $Description }
    Write-Output "PASS $Description"
}
function Click-Ui([string]$Caption, [string]$Element) {
    for ($goaAttempt=0; $goaAttempt -lt 24; $goaAttempt++) {
        $goaUi = Read-Ui
        $goaButtons = @($goaUi.Buttons | Where-Object { $_.Enabled -and $(if ($Element) { $_.Name -eq $Element } else { $_.Text -match $Caption }) })
        if ($goaButtons.Count -ne 1) { throw "Expected one enabled UI target: $Caption / $Element, got $($goaButtons.Count)" }
        $goaButton = $goaButtons[0]
        if ($goaButton.Visible) {
            & "$PSScriptRoot/qa-player.ps1" -Action Click -X ([int]($goaButton.Bounds.x+$goaButton.Bounds.width/2)) -Y ([int]($goaButton.Bounds.y+$goaButton.Bounds.height/2)) | Out-Null
            return
        }
        # Hidden targets in this scenario live in the right scroll panel.
        $goaDelta = if ($goaButton.Bounds.y -lt 150) { 360 } else { -360 }
        & "$PSScriptRoot/qa-player.ps1" -Action Scroll -X ($Width-75) -Y ($Height-150) -WheelDelta $goaDelta | Out-Null
    }
    throw "Could not scroll UI target into view: $Caption / $Element"
}
function Press-Ui([string]$Key) { & "$PSScriptRoot/qa-player.ps1" -Action Key -Key $Key | Out-Null }
function Read-TestSave {
    Click-Ui '^保存$'
    return Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Raw | ConvertFrom-Json
}
try {
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height
    $goaDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 200
        $goaUi = Read-Ui
        if ($goaUi.Width -eq $Width -and $goaUi.Height -eq $Height -and $goaUi.Revision -eq 0 -and $goaUi.Phase -eq 'HeroSelection') { break }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    Assert-Ui ($goaUi.Phase -eq 'HeroSelection' -and $goaUi.Revision -eq 0) 'Fresh isolated QA match'
    Click-Ui '^调试$'; Click-Ui '^自动选英雄与出生$'
    Assert-Ui ((Read-Ui).Phase -eq 'Planning') 'One-click preparation reaches planning'
    $goaColors = @('blue','red','green','gold')
    for ($goaSeat=0; $goaSeat -lt 4; $goaSeat++) {
        Press-Ui ([string]($goaSeat+1))
        Assert-Ui ((Read-Ui).Seat -eq $goaSeat) "Key $($goaSeat+1) switches seat immediately"
        Click-Ui -Element ('hand-'+$goaColors[$goaSeat])
        if ($goaSeat -lt 3) { Assert-Ui ((Read-Ui).Phase -eq 'Planning') "Selection $($goaSeat+1) remains private" }
    }
    $goaUi = Read-Ui
    Assert-Ui ($goaUi.Phase -eq 'Action' -and $goaUi.FilledPlayDots -eq 4) 'Fourth selection reveals all four colors without confirmation'
    $goaHeading = $goaUi.RevealedHeading
    $goaArea = $goaUi.BoardBounds.width*$goaUi.BoardBounds.height
    foreach ($goaSide in @('left','right','top','bottom')) { Click-Ui -Element ('toggle-'+$goaSide) }
    $goaUi = Read-Ui
    Assert-Ui (-not $goaUi.LeftExpanded -and -not $goaUi.RightExpanded -and -not $goaUi.TopExpanded -and -not $goaUi.BottomExpanded -and $goaUi.BoardBounds.width*$goaUi.BoardBounds.height -gt $goaArea*1.5) 'Four collapsed panels release map space'
    foreach ($goaSide in @('left','right','top','bottom')) { Click-Ui -Element ('toggle-'+$goaSide) }
    $goaUi = Read-Ui
    $goaCenterX = $goaUi.BoardBounds.x+$goaUi.BoardBounds.width/2
    $goaCenterY = $goaUi.BoardBounds.y+$goaUi.BoardBounds.height/2
    $goaAnchor = $goaUi.Cells | Sort-Object { [Math]::Pow($_.Center.x-$goaCenterX,2)+[Math]::Pow($_.Center.y-$goaCenterY,2) } | Select-Object -First 1
    & "$PSScriptRoot/qa-player.ps1" -Action Scroll -X ([int]$goaAnchor.Center.x) -Y ([int]$goaAnchor.Center.y) -WheelDelta 120 | Out-Null
    $goaZoomed = Read-Ui
    $goaAnchorAfter = $goaZoomed.Cells | Where-Object { $_.X -eq $goaAnchor.X -and $_.Y -eq $goaAnchor.Y }
    Assert-Ui ($goaZoomed.Zoom -gt $goaUi.Zoom -and [Math]::Abs($goaAnchorAfter.Center.x-$goaAnchor.Center.x) -lt 2 -and [Math]::Abs($goaAnchorAfter.Center.y-$goaAnchor.Center.y) -lt 2) 'Wheel zoom preserves the point beneath the cursor'
    & "$PSScriptRoot/qa-player.ps1" -Action Drag -X ([int]$goaCenterX) -Y ([int]$goaCenterY) -ToX ([int]$goaCenterX+80) -ToY ([int]$goaCenterY+15) | Out-Null
    $goaPanned = Read-Ui
    Press-Ui '3'; $goaUi = Read-Ui
    Assert-Ui ([Math]::Abs($goaPanned.Focus.x-$goaZoomed.Focus.x) -gt 1 -and $goaUi.Zoom -eq $goaPanned.Zoom -and $goaUi.Focus.x -eq $goaPanned.Focus.x -and $goaUi.Focus.y -eq $goaPanned.Focus.y) 'Panning and seat changes preserve the map viewport'
    $goaField = $goaUi.Fields | Where-Object Name -eq 'debug-gold-delta'
    & "$PSScriptRoot/qa-player.ps1" -Action Click -X ([int]($goaField.Bounds.x+$goaField.Bounds.width-70)) -Y ([int]($goaField.Bounds.y+$goaField.Bounds.height/2)) | Out-Null
    Press-Ui '1'; Click-Ui '^应用金币变化$'; $goaSave = Read-TestSave
    Assert-Ui ((Read-Ui).Seat -eq 2 -and $goaSave.Players[2].Gold -gt 0 -and $goaSave.Players[0].Gold -eq 0) 'Numeric input does not trigger a seat shortcut'
    Click-Ui '^在地图选择落点$'
    $goaUi = Read-Ui
    $goaTarget = $goaUi.Cells | Where-Object Legal | Sort-Object { [Math]::Pow($_.Center.x-$goaCenterX,2)+[Math]::Pow($_.Center.y-$goaCenterY,2) } | Select-Object -First 1
    & "$PSScriptRoot/qa-player.ps1" -Action Click -X ([int]$goaTarget.Center.x) -Y ([int]$goaTarget.Center.y) | Out-Null
    Assert-Ui ((Read-Ui).SelectedCell -eq "$($goaTarget.X),$($goaTarget.Y)") 'Zoomed map click selects the advertised legal hex'
    Click-Ui '^确认调试传送'; $goaSave = Read-TestSave
    $goaHero = $goaSave.Units | Where-Object { $_.Seat -eq 2 }
    Assert-Ui ($goaHero.Position.X -eq $goaTarget.X -and $goaHero.Position.Y -eq $goaTarget.Y) 'Teleport confirmation changes the selected hero'
    Click-Ui '^弃置所选牌$'
    Assert-Ui ((Read-Ui).DiscardDotCount -eq 1 -and (Read-Ui).FilledPlayDots -eq 4) 'Discard has a separate colored circle'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name "ui-smoke-${Width}x${Height}-discard" | Out-Null
    Click-Ui '^取回所选弃牌$'
    Assert-Ui ((Read-Ui).DiscardDotCount -eq 0) 'Recovery removes the discard circle without leaving an empty slot'
    Click-Ui '^行动$'
    for ($goaAction=0; $goaAction -lt 4; $goaAction++) {
        $goaUi = Read-Ui; Press-Ui ([string]($goaUi.ActiveSeat+1))
        Click-Ui '^放弃此牌行动$'; Click-Ui '^确认放弃此牌行动$'
    }
    $goaUi = Read-Ui
    Assert-Ui ($goaUi.Phase -eq 'Planning' -and $goaUi.Turn -eq 2 -and $goaUi.RevealedHeading -eq $goaHeading -and $goaUi.FilledPlayDots -eq 4) 'Previous revealed cards and play dots remain during next planning'
    $null = Read-TestSave
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name "ui-smoke-${Width}x${Height}-complete" | Out-Null
} catch {
    $goaChecks.Add([pscustomobject]@{ check='Scenario execution'; passed=$false; error=$_.Exception.Message })
    try { & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name "ui-smoke-${Width}x${Height}-failure" | Out-Null } catch {}
    throw
} finally {
    [pscustomobject]@{ startedUtc=$goaStarted.ToString('o'); finishedUtc=[DateTime]::UtcNow.ToString('o'); width=$Width; height=$Height; method='Real foreground mouse and keyboard input'; checks=$goaChecks } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $goaReportPath -Encoding utf8
    Write-Output "UI report: $goaReportPath"
}

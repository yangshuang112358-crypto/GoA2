# Shared real-input helpers. Dot-source after defining goaRoot, goaChecks, Width and Height.
function Read-Ui { Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json') -Raw | ConvertFrom-Json }
function Check([bool]$Condition,[string]$Description) {
    $goaChecks.Add([pscustomobject]@{check=$Description;passed=$Condition})
    if (-not $Condition) { throw $Description }
    Write-Output "PASS $Description"
}
function Click([string]$Caption,[string]$Element) {
    if ($Element) { & "$PSScriptRoot/qa-player.ps1" -Action Click -Element $Element -AutoScroll | Out-Null }
    else { & "$PSScriptRoot/qa-player.ps1" -Action Click -Caption $Caption -AutoScroll | Out-Null }
}
function Save-State {
    Click '^保存$'
    Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Raw | ConvertFrom-Json
}
function Close-QaPlayer {
    $goaPidFile=Join-Path $goaRoot 'artifacts/unity/player.pid'
    if (-not (Test-Path -LiteralPath $goaPidFile)) { return }
    $goaProcess=Get-Process -Id ([int](Get-Content -LiteralPath $goaPidFile)) -ErrorAction SilentlyContinue
    if (-not $goaProcess -or $goaProcess.ProcessName -ne 'Goa2V1') { return }
    if ($goaProcess.Path -ne (Join-Path $goaRoot 'artifacts/player/Goa2V1.exe')) { throw 'The tracked process is outside the QA Player path.' }
    [void]$goaProcess.CloseMainWindow()
    if (-not $goaProcess.WaitForExit(5000)) { throw 'The QA Player did not close.' }
}
function Open-Save([string]$Path,[string]$Phase,[long]$Revision) {
    Close-QaPlayer
    & "$PSScriptRoot/run-player.ps1" -Qa -Width $Width -Height $Height -LoadSave $Path
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        try { $goaUi=Read-Ui } catch { continue }
        if ($goaUi.Phase -eq $Phase -and $goaUi.Revision -eq $Revision) { break }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    Check ($goaUi.Phase -eq $Phase -and $goaUi.Revision -eq $Revision) "Save restores at $Phase revision $Revision"
}
function Open-Setup([string]$Scenario,[int]$Steps) {
    Close-QaPlayer
    $goaSetup=Get-Content -LiteralPath (Join-Path $goaRoot "tests/scenarios/$Scenario.json") -Raw | ConvertFrom-Json
    $goaSetup.Id="ui-$Scenario-setup"; $goaSetup.Steps=@($goaSetup.Steps | Select-Object -First $Steps)
    $goaSetupPath=Join-Path $goaRoot "artifacts/unity/$($goaSetup.Id).json"
    $goaSetup | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $goaSetupPath -Encoding utf8
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaSetupPath
    $goaRun=Get-ChildItem -LiteralPath (Join-Path $goaRoot "artifacts/scenarios/$($goaSetup.Id)/headless") -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    Open-Save (Join-Path $goaRun.FullName 'report.json.save.json') 'Action' $Steps
}
function Select-Cell([int]$X,[int]$Y) {
    $goaUi=Read-Ui
    $goaCell=$goaUi.Cells | Where-Object { $_.X -eq $X -and $_.Y -eq $Y }
    if (-not $goaCell -or -not $goaCell.Legal) { throw "Expected a legal target at $X,$Y" }
    $goaBounds=$goaUi.BoardBounds
    if ($goaCell.Center.x -lt ($goaBounds.x+2) -or $goaCell.Center.x -gt ($goaBounds.x+$goaBounds.width-2) -or
        $goaCell.Center.y -lt ($goaBounds.y+2) -or $goaCell.Center.y -gt ($goaBounds.y+$goaBounds.height-2)) {
        throw "Legal target $X,$Y is outside the visible map. Pan, zoom or collapse a panel before clicking."
    }
    & "$PSScriptRoot/qa-player.ps1" -Action Click -X ([int]$goaCell.Center.x) -Y ([int]$goaCell.Center.y) | Out-Null
}
function Pass-Seat([int]$Key) {
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key $Key | Out-Null
    Click '^放弃此牌行动$'; Click '^确认放弃此牌行动$'
}

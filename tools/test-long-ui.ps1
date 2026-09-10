param(
    [Parameter(Mandatory)][string]$SavePath,
    [ValidateRange(1152,3840)][int]$Width=1152,
    [ValidateRange(768,2160)][int]$Height=768,
    [ValidateRange(500,10000)][int]$InteractionBudgetMs=1500
)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaInput=(Resolve-Path -LiteralPath $SavePath).Path
$goaInitial=Get-Content -LiteralPath $goaInput -Raw | ConvertFrom-Json
if (-not $goaInitial.Sandbox -or [int]$goaInitial.Phase -ne 2) { throw 'Choose an existing sandbox save in Planning.' }
$goaInputHash=(Get-FileHash -LiteralPath $goaInput).Hash.ToLowerInvariant()
$goaOutput=Join-Path $goaRoot ('artifacts/long-ui/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $goaOutput | Out-Null
$goaChecks=[Collections.Generic.List[object]]::new()
$goaMeasurements=[Collections.Generic.List[object]]::new()
$goaStarted=[DateTime]::UtcNow
$goaFailure=''
. "$PSScriptRoot/ui-qa-common.ps1"
function Wait-LongUi([DateTime]$After,[int]$Seat,[long]$Revision) {
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 30
        try {
            $goaSnapshot=Read-Ui
            $goaFresh=(Get-Item -LiteralPath (Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json')).LastWriteTimeUtc -ge $After
        } catch { continue }
        if ($goaFresh -and $goaSnapshot.Seat -eq $Seat -and $goaSnapshot.Revision -eq $Revision) { return $goaSnapshot }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    throw 'The long-match UI did not reach the expected fresh seat and revision.'
}
try {
    Open-Save $goaInput 'Planning' $goaInitial.Revision
    $goaBefore=Read-Ui
    foreach ($goaKey in @(4,1,3,2,4,2,3,1)) {
        $goaAt=[DateTime]::UtcNow; $goaWatch=[Diagnostics.Stopwatch]::StartNew()
        & "$PSScriptRoot/qa-player.ps1" -Action Key -Key $goaKey.ToString() | Out-Null
        $goaUi=Wait-LongUi $goaAt ($goaKey-1) $goaInitial.Revision
        $goaWatch.Stop()
        $goaMeasurements.Add([pscustomobject]@{action=('Seat '+$goaKey);milliseconds=$goaWatch.Elapsed.TotalMilliseconds})
        $goaRoster=@($goaUi.Buttons | Where-Object { $_.Name -eq ('seat-'+$goaKey) -and $_.Visible }).Count -eq 1
        $goaMap=$goaUi.Zoom -eq $goaBefore.Zoom -and $goaUi.Focus.x -eq $goaBefore.Focus.x -and $goaUi.Focus.y -eq $goaBefore.Focus.y
        Check ($goaRoster -and $goaMap) ('Long match: selected seat '+$goaKey+' is visible and the map remains fixed')
        Check ($goaWatch.Elapsed.TotalMilliseconds -le $InteractionBudgetMs) ('Long match: seat '+$goaKey+' refresh is within the interaction budget')
    }
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'long-match-roster' | Out-Null
    Click '^调试$'
    $goaAt=[DateTime]::UtcNow; $goaWatch=[Diagnostics.Stopwatch]::StartNew()
    Click '^\+5$'
    $goaUi=Wait-LongUi $goaAt 0 ($goaInitial.Revision+1)
    $goaWatch.Stop()
    $goaMeasurements.Add([pscustomobject]@{action='Add five gold';milliseconds=$goaWatch.Elapsed.TotalMilliseconds})
    Check ($goaWatch.Elapsed.TotalMilliseconds -le $InteractionBudgetMs) 'Long match: a real debug gold command remains within the interaction budget'
    $goaSaved=Save-State
    Check ($goaSaved.Revision -eq $goaInitial.Revision+1 -and $goaSaved.Players[0].Gold -eq $goaInitial.Players[0].Gold+5) 'Long match: gold changes once and the original history remains accepted'
    $goaContinued=Join-Path $goaOutput 'continued.save.json'
    Copy-Item -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json') -Destination $goaContinued
    $goaHash=(Get-FileHash -LiteralPath $goaContinued).Hash
    Open-Save $goaContinued 'Planning' ($goaInitial.Revision+1)
    $goaRestored=Save-State
    Check ((Get-FileHash -LiteralPath (Join-Path $goaRoot 'artifacts/unity/qa-save.json')).Hash -eq $goaHash) 'Long match: the new command restores to the identical complete save'
    Check ((Get-FileHash -LiteralPath $goaInput).Hash.ToLowerInvariant() -eq $goaInputHash) 'The supplied long save is unchanged'
} catch {
    $goaFailure=$_.Exception.Message
    $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$goaFailure})
    throw
} finally {
    Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/unity') -File | Where-Object { $_.LastWriteTimeUtc -ge $goaStarted -and $_.Name -in @('render-latest.png','render-latest.ui.json','long-match-roster.png','player.log') } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $goaOutput $_.Name) }
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');inputSha256=$goaInputHash;revision=$goaInitial.Revision;width=$Width;height=$Height;budgetMs=$InteractionBudgetMs;measurements=$goaMeasurements;checks=$goaChecks;passed=($goaFailure -eq '');error=$goaFailure;method='Wall time includes real input helper, its 350 ms delay, and a fresh rendered QA snapshot'} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaOutput 'report.json') -Encoding utf8
    Write-Output "Long-match UI evidence: $goaOutput"
    Close-QaPlayer
}

param()
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaChecks=[Collections.Generic.List[object]]::new()
$goaOutput=Join-Path $goaRoot ('artifacts/playback-ui/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $goaOutput | Out-Null
$goaStarted=[DateTime]::UtcNow
$goaVisualStarted=$goaStarted
$goaJob=$null
$goaFailure=''
$goaScenario=Join-Path $goaRoot 'tests/scenarios/sandbox-smoke.json'
$goaInputHash=(Get-FileHash -LiteralPath $goaScenario).Hash
. "$PSScriptRoot/ui-qa-common.ps1"
function Wait-Playback([scriptblock]$Condition) {
    $goaDeadline=[DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 100
        try {
            $goaSnapshot=Read-Ui
            $goaFresh=(Get-Item -LiteralPath (Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json')).LastWriteTimeUtc -ge $goaVisualStarted
            if ($goaFresh -and (& $Condition $goaSnapshot)) { return $goaSnapshot }
        } catch { }
    } while ([DateTime]::UtcNow -lt $goaDeadline)
    throw 'The playback UI did not reach the expected state.'
}
try {
    Close-QaPlayer
    & "$PSScriptRoot/run-scenarios.ps1" -Scenario $goaScenario
    $goaHeadless=Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/scenarios/sandbox-smoke/headless') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $goaExpected=Get-Content -LiteralPath (Join-Path $goaHeadless.FullName 'report.json') -Raw | ConvertFrom-Json
    # A separate PowerShell runspace runs the existing launcher while this one sends real input.
    # The only visible window is the requested Player; no helper terminal is opened.
    $goaVisualStarted=[DateTime]::UtcNow
    $goaJob=Start-ThreadJob -ArgumentList $PSScriptRoot,$goaScenario -ScriptBlock {
        param($goaTools,$goaInput)
        & (Join-Path $goaTools 'run-scenarios.ps1') -Scenario $goaInput -Visual -Paused -KeepOpen -Delay 1 -TimeoutSeconds 180
    }
    $goaInitial=Wait-Playback { param($ui) $ui.Revision -eq 0 -and @($ui.Buttons|Where-Object { $_.Name -eq 'scenario-step' -and $_.Enabled -and $_.Visible }).Count -eq 1 }
    Check ($goaInitial.Phase -eq 'HeroSelection') 'Paused playback opens before the first command'
    Start-Sleep -Milliseconds 1800
    Check ((Read-Ui).Revision -eq 0) 'A paused scene does not advance by itself'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 4 | Out-Null
    Check ((Read-Ui).Seat -eq 3 -and (Read-Ui).Revision -eq 0) 'The observer can switch characters while playback is paused'
    $goaProtected=@((Read-Ui).Buttons|Where-Object {$_.Text -in @('读取','新对局')})
    Check ($goaProtected.Count -eq 2 -and @($goaProtected|Where-Object Enabled).Count -eq 0) 'Loading and replacing the match remain disabled during scene ownership'
    $goaHeroName=((Get-Content -LiteralPath (Join-Path $goaRoot 'content/canonical/heroes.json') -Raw | ConvertFrom-Json).heroes | Where-Object hero_id -eq 'wasp').name
    Click ('^'+[regex]::Escape($goaHeroName)+'$')
    $goaConfirmName='确认选择 '+$goaHeroName.Split('·')[-1]
    $null=Wait-Playback {param($ui) @($ui.Buttons|Where-Object {$_.Text -eq $goaConfirmName -and $_.Enabled}).Count -eq 1}
    Click ('^'+[regex]::Escape($goaConfirmName)+'$')
    Check ((Read-Ui).Revision -eq 0 -and (Read-Ui).Phase -eq 'HeroSelection') 'A real manual command cannot alter the scene behind the playback controls'
    Click -Element 'scenario-step'
    $null=Wait-Playback {param($ui) $ui.Revision -eq 1 -and $ui.Phase -eq 'Planning'}
    Start-Sleep -Milliseconds 1800
    Check ((Read-Ui).Revision -eq 1) 'Single-step executes exactly one command and stays paused'
    Click -Element 'scenario-pause'
    $null=Wait-Playback {param($ui) $ui.Revision -ge 2}
    Click -Element 'scenario-pause'
    $goaPausedRevision=(Read-Ui).Revision
    Start-Sleep -Milliseconds 2600
    Check ((Read-Ui).Revision -eq $goaPausedRevision -and @((Read-Ui).Buttons|Where-Object {$_.Name -eq 'scenario-step' -and $_.Enabled}).Count -eq 1) 'Pause interrupts automatic playback and restores the single-step control'
    Click -Element 'scenario-pause'
    $null=Wait-Playback {param($ui) @($ui.Buttons|Where-Object {$_.Name -eq 'scenario-finish' -and $_.Enabled}).Count -eq 1}
    $null=Wait-Job -Job $goaJob -Timeout 10
    if ($goaJob.State -ne 'Completed') { throw 'The existing scene launcher did not complete successfully.' }
    Receive-Job -Job $goaJob -ErrorAction Stop | Tee-Object -FilePath (Join-Path $goaOutput 'launcher.log')
    $goaVisible=Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/scenarios/sandbox-smoke/visual') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $goaReport=Get-Content -LiteralPath (Join-Path $goaVisible.FullName 'report.json') -Raw | ConvertFrom-Json
    Check ($goaReport.Passed -and $goaReport.Complete -and $goaReport.Steps.Count -eq 9 -and $goaReport.FinalStateHash -ceq $goaExpected.FinalStateHash) 'Stepping, pausing and resuming reach the same complete state as headless playback'
    $goaFrozenHash=(Get-FileHash -LiteralPath (Join-Path $goaVisible.FullName 'report.json.save.json')).Hash
    Check (@((Read-Ui).Buttons|Where-Object {$_.Name -in @('scenario-step','scenario-pause') -and $_.Enabled}).Count -eq 0) 'Completed playback disables further automated steps'
    & "$PSScriptRoot/qa-player.ps1" -Action Capture -Name 'playback-complete' | Out-Null
    Click -Element 'scenario-finish'
    Check (@((Read-Ui).Buttons|Where-Object {$_.Name -like 'scenario-*'}).Count -eq 0) 'Switching to manual control removes the playback toolbar'
    & "$PSScriptRoot/qa-player.ps1" -Action Key -Key 1 | Out-Null
    Click '^调试$';Click '^\+5$';Click '^保存$'
    $goaManual=Get-Content -LiteralPath (Join-Path $goaVisible.FullName 'manual-save.json') -Raw | ConvertFrom-Json
    Check ($goaManual.Players[0].Gold -eq 15 -and $goaManual.Revision -eq 8) 'The completed position accepts and saves one new manual gold command'
    Check ((Get-FileHash -LiteralPath (Join-Path $goaVisible.FullName 'report.json.save.json')).Hash -ceq $goaFrozenHash -and (Get-FileHash -LiteralPath $goaScenario).Hash -ceq $goaInputHash) 'Manual continuation leaves the original scenario and verified final snapshot untouched'
    Copy-Item -LiteralPath (Join-Path $goaVisible.FullName 'report.json'),(Join-Path $goaVisible.FullName 'run.json'),(Join-Path $goaVisible.FullName 'manual-save.json') -Destination $goaOutput
} catch { $goaFailure=$_.Exception.Message; $goaChecks.Add([pscustomobject]@{check='Execution';passed=$false;error=$goaFailure});throw }
finally {
    Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/unity') -File | Where-Object {$_.LastWriteTimeUtc -ge $goaStarted -and $_.Name -in @('render-latest.png','render-latest.ui.json','playback-complete.png')} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $goaOutput}
    [pscustomobject]@{startedUtc=$goaStarted.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o');passed=($goaFailure -eq '');checks=$goaChecks;error=$goaFailure;inputSha256=$goaInputHash.ToLowerInvariant()} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaOutput 'controls.json') -Encoding utf8
    Close-QaPlayer
    if ($goaJob) { if ($goaJob.State -in @('Running','NotStarted')) {Stop-Job -Job $goaJob};Remove-Job -Job $goaJob -Force }
    Write-Output "Playback UI evidence: $goaOutput"
}

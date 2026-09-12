param(
    [ValidateSet('all','smoke','presets','recovery','gallery','battlefield','combat','upgrade','round-minion','auras','barriers','marksman','adjacent-attacks','conditional-defenses','optional-discard','riposte-choice','sneak','loyal')]
    [string[]]$Case = @('all'),
    [ValidateRange(1152,3840)][int]$Width = 1280,
    [ValidateRange(768,2160)][int]$Height = 800,
    [switch]$ContinueOnFailure
)
$ErrorActionPreference = 'Stop'
$goaRoot = Split-Path -Parent $PSScriptRoot
$goaCases = [ordered]@{
    loyal = @('test-loyal-ui.ps1','loyal-ui')
    sneak = @('test-sneak-ui.ps1','sneak-ui')
    smoke = @('test-player-ui.ps1','ui-smoke')
    presets = @('test-presets-ui.ps1','presets-ui')
    recovery = @('test-recovery-ui.ps1','recovery-ui')
    gallery = @('test-gallery-ui.ps1','gallery-ui')
    battlefield = @('test-battlefield-ui.ps1','battlefield-ui')
    combat = @('test-combat-ui.ps1','combat-ui')
    upgrade = @('test-upgrade-ui.ps1','upgrade-ui')
    'round-minion' = @('test-round-minion-ui.ps1','round-minion-ui')
    auras = @('test-auras-ui.ps1','auras-ui')
    barriers = @('test-barriers-ui.ps1','barriers-ui')
    marksman = @('test-marksman-ui.ps1','marksman-ui')
    'adjacent-attacks' = @('test-adjacent-attacks-ui.ps1','adjacent-attacks-ui')
    'conditional-defenses' = @('test-conditional-defenses-ui.ps1','conditional-defenses-ui')
    'optional-discard' = @('test-optional-discard-ui.ps1','optional-discard-ui')
    'riposte-choice' = @('test-riposte-choice-ui.ps1','riposte-choice-ui')
}
if ($Case -contains 'all' -and $Case.Count -ne 1) { throw 'Use all by itself, or select individual cases.' }
$goaSelected = if ($Case -contains 'all') { @($goaCases.Keys) } else { @($Case | Select-Object -Unique) }
$goaPlayerRoot = Join-Path $goaRoot 'artifacts/player'
if (-not (Test-Path -LiteralPath (Join-Path $goaPlayerRoot 'build-info.json'))) { throw 'Build the Player before running the UI suite.' }
. "$PSScriptRoot/ui-qa-common.ps1"
function Get-PlayerEvidence {
    @(Get-ChildItem -LiteralPath $goaPlayerRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{path=[IO.Path]::GetRelativePath($goaPlayerRoot,$_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
}
$goaMutex = [Threading.Mutex]::new($false,'Local\Goa2V1UiSuite')
$goaLocked = $false
$goaSuite = $null
$goaOutput = $null
try {
    try { $goaLocked = $goaMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $goaLocked = $true }
    if (-not $goaLocked) { throw 'Another Goa2V1 UI suite is running.' }
    $goaBatch = [Guid]::NewGuid().ToString('N')
    $goaOutput = Join-Path $goaRoot "artifacts/ui-suites/$goaBatch"
    New-Item -ItemType Directory -Path $goaOutput | Out-Null
    $goaPayload = Get-PlayerEvidence
    $goaPayload | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $goaOutput 'player-before.json') -Encoding utf8
    $goaInputs = @('qa-player.ps1','qa-text.ps1','run-player.ps1','run-scenarios.ps1','ui-qa-common.ps1','test-ui-suite.ps1') + @($goaSelected | ForEach-Object { $goaCases[$_][0] })
    $goaInputEvidence = @($goaInputs | Select-Object -Unique | ForEach-Object {
        $goaPath = Join-Path $PSScriptRoot $_
        [pscustomobject]@{path="tools/$_";sha256=(Get-FileHash -LiteralPath $goaPath -Algorithm SHA256).Hash.ToLowerInvariant()}
    })
    $goaSuite = [ordered]@{
        batchId=$goaBatch; startedUtc=[DateTime]::UtcNow.ToString('o'); finishedUtc=$null
        width=$Width; height=$Height; mode='Real foreground mouse and keyboard'; selectedCases=$goaSelected
        gitCommit=(& git -C $goaRoot rev-parse HEAD); worktreeStatus=@(& git -C $goaRoot status --short)
        inputs=$goaInputEvidence; cases=[Collections.Generic.List[object]]::new()
        playerUnchanged=$false; passed=$false; failure=$null
    }
    foreach ($goaName in $goaSelected) {
        $goaCaseOutput = Join-Path $goaOutput $goaName
        New-Item -ItemType Directory -Path $goaCaseOutput | Out-Null
        $goaCaseStarted = [DateTime]::UtcNow
        $goaEntry = [ordered]@{name=$goaName;startedUtc=$goaCaseStarted.ToString('o');finishedUtc=$null;passed=$false;checks=0;failedChecks=0;error=$null}
        try {
            Close-QaPlayer
            Write-Output "Running UI case $goaName at ${Width}x${Height}"
            & (Join-Path $PSScriptRoot $goaCases[$goaName][0]) -Width $Width -Height $Height 2>&1 | Tee-Object -FilePath (Join-Path $goaCaseOutput 'output.log')
            $goaReportPath = Join-Path $goaRoot ("artifacts/unity/"+$goaCases[$goaName][1]+"-${Width}x${Height}.json")
            $goaReportFile = Get-Item -LiteralPath $goaReportPath
            if ($goaReportFile.LastWriteTimeUtc -lt $goaCaseStarted) { throw 'The UI report is stale.' }
            $goaReport = Get-Content -LiteralPath $goaReportPath -Raw | ConvertFrom-Json
            # ConvertFrom-Json can already return DateTime values. Parsing their display text loses fractions.
            $goaReportStart = ([DateTime]$goaReport.startedUtc).ToUniversalTime()
            $goaReportEnd = ([DateTime]$goaReport.finishedUtc).ToUniversalTime()
            if ($goaReportStart -lt $goaCaseStarted -or $goaReportEnd -lt $goaReportStart) { throw 'The UI report timestamps do not describe this run.' }
            $goaEntry.checks = @($goaReport.checks).Count
            $goaEntry.failedChecks = @($goaReport.checks | Where-Object { $_.passed -isnot [bool] -or -not $_.passed }).Count
            if ($goaEntry.checks -eq 0 -or $goaEntry.failedChecks -gt 0) { throw 'The UI report is empty or contains failed checks.' }
            $goaEntry.passed = $true
        } catch {
            $goaEntry.error = $_.Exception.Message
            Write-Warning "UI case ${goaName}: $($goaEntry.error)"
        } finally {
            $goaEntry.finishedUtc = [DateTime]::UtcNow.ToString('o')
            $goaCaseReportPath = Join-Path $goaRoot ("artifacts/unity/"+$goaCases[$goaName][1]+"-${Width}x${Height}.json")
            if ((Test-Path -LiteralPath $goaCaseReportPath) -and (Get-Item -LiteralPath $goaCaseReportPath).LastWriteTimeUtc -ge $goaCaseStarted) {
                try {
                    $goaPartialReport = Get-Content -LiteralPath $goaCaseReportPath -Raw | ConvertFrom-Json
                    $goaEntry.checks = @($goaPartialReport.checks).Count
                    $goaEntry.failedChecks = @($goaPartialReport.checks | Where-Object { $_.passed -isnot [bool] -or -not $_.passed }).Count
                } catch { } # Preserve the original failure and copy the malformed report below.
            }
            $goaSuite.cases.Add([pscustomobject]$goaEntry)
            # Retain only fresh evidence; a failed launch must never reuse an earlier report.
            $goaExpectedReport = $goaCases[$goaName][1]+"-${Width}x${Height}.json"
            Get-ChildItem -LiteralPath (Join-Path $goaRoot 'artifacts/unity') -File | Where-Object {
                $_.LastWriteTimeUtc -ge $goaCaseStarted -and ($_.Name -eq $goaExpectedReport -or $_.Extension -eq '.png' -or $_.Name -in @('qa-save.json','render-latest.ui.json','player.log'))
            } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $goaCaseOutput $_.Name) }
            $goaSuite | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $goaOutput 'suite.json') -Encoding utf8
        }
        if (-not $goaEntry.passed -and -not $ContinueOnFailure) { break }
    }
    $goaAfter = Get-PlayerEvidence
    $goaAfter | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $goaOutput 'player-after.json') -Encoding utf8
    $goaSuite.playerUnchanged = (($goaPayload | ConvertTo-Json -Depth 5 -Compress) -ceq ($goaAfter | ConvertTo-Json -Depth 5 -Compress))
    $goaSuite.passed = $goaSuite.playerUnchanged -and $goaSuite.cases.Count -eq $goaSelected.Count -and @($goaSuite.cases | Where-Object { -not $_.passed }).Count -eq 0
    if (-not $goaSuite.playerUnchanged) { throw 'Player files changed during the UI suite.' }
    if (-not $goaSuite.passed) { throw 'The UI suite did not pass every selected case. See suite.json.' }
} catch {
    if ($null -ne $goaSuite) { $goaSuite.failure=$_.Exception.Message; $goaSuite.passed=$false }
    throw
} finally {
    if ($null -ne $goaSuite) {
        $goaSuite.finishedUtc=[DateTime]::UtcNow.ToString('o')
        $goaSuite | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $goaOutput 'suite.json') -Encoding utf8
        Write-Output "UI suite report: $(Join-Path $goaOutput 'suite.json')"
    }
    if ($goaLocked) { $goaMutex.ReleaseMutex() }
    $goaMutex.Dispose()
}

param(
    [Parameter(Mandatory)][string]$Archive,
    [string]$Scenario = 'tests/scenarios/throwing-axe-reflection.json',
    [string]$PythonExe,
    [switch]$Visual,
    [ValidateRange(0,10)][double]$Delay=0.1,
    [ValidateRange(10,3600)][int]$TimeoutSeconds=300
)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaArchive=(Resolve-Path -LiteralPath $Archive).Path
$goaInput=(Resolve-Path -LiteralPath $Scenario).Path
if (-not $PythonExe) {
    $goaBundled=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
    $PythonExe=if (Test-Path -LiteralPath $goaBundled) { $goaBundled } else { 'python' }
}
# Verify names and exact inventory before extraction. Every run gets a new directory.
$goaVerified=& $PythonExe -B (Join-Path $PSScriptRoot 'player_package.py') verify-archive $goaArchive
if ($LASTEXITCODE -ne 0) { throw 'Archive verification failed; nothing was extracted or launched.' }
$goaBase=[IO.Path]::GetFullPath((Join-Path $goaRoot 'artifacts/release-smoke'))
$goaOutput=[IO.Path]::GetFullPath((Join-Path $goaBase ([Guid]::NewGuid().ToString('N'))))
if (-not $goaOutput.StartsWith($goaBase+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Smoke output is outside the intended directory.' }
New-Item -ItemType Directory -Path $goaOutput | Out-Null
$goaExtract=Join-Path $goaOutput 'extracted'
Expand-Archive -LiteralPath $goaArchive -DestinationPath $goaExtract
$goaPlayer=Join-Path $goaExtract 'Goa2V1'
$goaVerifiedBuild=& $PythonExe -B (Join-Path $PSScriptRoot 'player_package.py') verify-build $goaPlayer
if ($LASTEXITCODE -ne 0) { throw 'Extracted payload verification failed.' }
$goaCopiedInput=Join-Path $goaOutput 'scenario.json'
Copy-Item -LiteralPath $goaInput -Destination $goaCopiedInput
$goaReportPath=Join-Path $goaOutput 'report.json'
$goaLogPath=Join-Path $goaOutput 'player.log'
$goaArguments='-goaScenario "'+$goaCopiedInput+'" -goaScenarioReport "'+$goaReportPath+'" -goaSavePath "'+(Join-Path $goaOutput 'manual-save.json')+'" -logFile "'+$goaLogPath+'"'
if ($Visual) { $goaArguments+=' -screen-fullscreen 0 -screen-width 1600 -screen-height 1000 -goaScenarioQuit -goaScenarioDelay '+$Delay.ToString([Globalization.CultureInfo]::InvariantCulture) }
else { $goaArguments+=' -batchmode -nographics' }
$goaEvidence=[ordered]@{
    startedUtc=[DateTime]::UtcNow.ToString('o');finishedUtc=$null;passed=$false;error=''
    archive=$goaArchive;archiveSha256=(Get-FileHash -LiteralPath $goaArchive).Hash.ToLowerInvariant()
    inputSha256=(Get-FileHash -LiteralPath $goaCopiedInput).Hash.ToLowerInvariant();visual=[bool]$Visual
    inventorySha256=(Get-FileHash -LiteralPath (Join-Path $goaPlayer 'build-info.json')).Hash.ToLowerInvariant()
    verification=($goaVerifiedBuild | ConvertFrom-Json);exitCode=$null;stateHash='';steps=0
}
try {
    $goaStart=@{FilePath=(Join-Path $goaPlayer 'Goa2V1.exe');ArgumentList=$goaArguments;WorkingDirectory=$goaExtract;PassThru=$true}
    if (-not $Visual) { $goaStart.WindowStyle='Hidden' }
    $goaProcess=Start-Process @goaStart
    $goaDeadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $goaProcess.HasExited) {
        if ([DateTime]::UtcNow -ge $goaDeadline) {
            if (-not $Visual) { $goaProcess.Kill() }
            throw 'Extracted Player scenario timed out.'
        }
        Start-Sleep -Milliseconds 150
    }
    $goaEvidence.exitCode=$goaProcess.ExitCode
    if (-not (Test-Path -LiteralPath $goaReportPath)) { throw 'Extracted Player did not create a report.' }
    $goaReport=Get-Content -LiteralPath $goaReportPath -Raw | ConvertFrom-Json
    if ($goaProcess.ExitCode -ne 0 -or -not $goaReport.Passed -or -not $goaReport.Complete) { throw 'Extracted Player scenario failed.' }
    $goaEvidence.stateHash=$goaReport.FinalStateHash; $goaEvidence.steps=$goaReport.Steps.Count
    $goaEvidence.passed=$true
    Write-Output "PASS extracted Player: $($goaEvidence.steps) steps, state $($goaEvidence.stateHash)"
} catch { $goaEvidence.error=$_.Exception.Message; throw }
finally {
    $goaEvidence.finishedUtc=[DateTime]::UtcNow.ToString('o')
    $goaEvidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaOutput 'smoke.json') -Encoding utf8
    Write-Output "Release smoke evidence: $goaOutput"
}

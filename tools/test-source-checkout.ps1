param(
    [string]$Commit='HEAD',
    [string]$UnityExe,
    [string]$PythonExe,
    [switch]$UnityTests
)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaRevision=(& git -C $goaRoot rev-parse --verify --end-of-options ($Commit+'^{commit}'))
if ($LASTEXITCODE -ne 0 -or $goaRevision -notmatch '^[a-f0-9]{40}$') { throw 'Select an existing local commit.' }
$goaBase=[IO.Path]::GetFullPath((Join-Path $goaRoot 'artifacts/source-checkouts'))
$goaOutput=[IO.Path]::GetFullPath((Join-Path $goaBase ([Guid]::NewGuid().ToString('N'))))
if (-not $goaOutput.StartsWith($goaBase+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Output is outside the intended source-checkout directory.' }
New-Item -ItemType Directory -Path $goaOutput | Out-Null
$goaArchive=Join-Path $goaOutput 'source.zip'
$goaCheckout=Join-Path $goaOutput 'Goa2V1'
if (-not $PythonExe) {
    $goaBundled=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
    $PythonExe=if (Test-Path -LiteralPath $goaBundled) { $goaBundled } else { 'python' }
}
$goaEvidence=[ordered]@{
    gitCommit=$goaRevision;startedUtc=[DateTime]::UtcNow.ToString('o');finishedUtc=$null
    archiveSha256='';sourceDirectory=$goaCheckout;withoutLibrary=$false;withoutPreparedContent=$false
    dataPassed=$false;unityTestsRequested=[bool]$UnityTests;unityTestsPassed=$null
    buildPassed=$false;scenarioPassed=$false;stateHash='';passed=$false;error=''
}
$goaEntered=$false
try {
    & git -C $goaRoot archive --format=zip --prefix=Goa2V1/ --output=$goaArchive $goaRevision
    if ($LASTEXITCODE -ne 0) { throw 'Git could not archive the selected commit.' }
    $goaEvidence.archiveSha256=(Get-FileHash -LiteralPath $goaArchive).Hash.ToLowerInvariant()
    Expand-Archive -LiteralPath $goaArchive -DestinationPath $goaOutput
    $goaEvidence.withoutLibrary=-not (Test-Path -LiteralPath (Join-Path $goaCheckout 'unity/Library'))
    $goaEvidence.withoutPreparedContent=(-not (Test-Path -LiteralPath (Join-Path $goaCheckout 'unity/Assets/StreamingAssets/Goa2'))) -and (-not (Test-Path -LiteralPath (Join-Path $goaCheckout 'unity/Assets/StreamingAssets/Goa2Debug')))
    if (-not $goaEvidence.withoutLibrary -or -not $goaEvidence.withoutPreparedContent) { throw 'The source archive unexpectedly includes generated Unity data.' }
    Push-Location -LiteralPath $goaCheckout; $goaEntered=$true
    $goaValidate=@{}
    if ($PythonExe) { $goaValidate.PythonExe=$PythonExe }
    & (Join-Path $goaCheckout 'tools/validate.ps1') @goaValidate
    if ($LASTEXITCODE -ne 0) { throw 'Data, documentation or Python tool validation failed in the source copy.' }
    $goaEvidence.dataPassed=$true
    if ($UnityTests) {
        $goaTest=@{ReportName='fresh-source-all'}
        if ($UnityExe) { $goaTest.UnityExe=$UnityExe }
        & (Join-Path $goaCheckout 'tools/test-unity.ps1') @goaTest
        $goaEvidence.unityTestsPassed=$true
    }
    $goaBuild=@{Task='BuildWindows'}
    if ($UnityExe) { $goaBuild.UnityExe=$UnityExe }
    & (Join-Path $goaCheckout 'tools/build-unity.ps1') @goaBuild
    $goaEvidence.buildPassed=$true
    & (Join-Path $goaCheckout 'tools/run-scenarios.ps1') -Scenario 'tests/scenarios/throwing-axe-reflection.json'
    $goaRun=Get-ChildItem -LiteralPath (Join-Path $goaCheckout 'artifacts/scenarios/throwing-axe-reflection/headless') -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $goaReport=Get-Content -LiteralPath (Join-Path $goaRun.FullName 'report.json') -Raw | ConvertFrom-Json
    if (-not $goaReport.Passed -or -not $goaReport.Complete -or $goaReport.Steps.Count -ne 20) { throw 'The freshly built Player scenario did not complete.' }
    $goaEvidence.scenarioPassed=$true; $goaEvidence.stateHash=$goaReport.FinalStateHash
    $goaEvidence.passed=$true
} catch { $goaEvidence.error=$_.Exception.Message; throw }
finally {
    if ($goaEntered) { Pop-Location }
    $goaEvidence.finishedUtc=[DateTime]::UtcNow.ToString('o')
    $goaEvidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $goaOutput 'checkout.json') -Encoding utf8
    Write-Output "Source checkout evidence: $goaOutput"
}

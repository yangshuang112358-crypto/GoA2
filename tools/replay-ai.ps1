param(
    [Parameter(Mandatory)][string]$Episode,
    [Parameter(Mandatory)][string]$Player,
    [string]$BuildSourceRoot,
    [string]$Output,
    [ValidateRange(10,3600)][int]$TimeoutSeconds=300,
    [switch]$Visual
)
$ErrorActionPreference='Stop'
$aiRoot=Split-Path -Parent $PSScriptRoot
$aiEpisode=(Resolve-Path -LiteralPath $Episode).Path
$aiPlayer=(Resolve-Path -LiteralPath $Player).Path
$aiBuild=Get-Content -LiteralPath (Join-Path (Split-Path -Parent $aiPlayer) 'build-info.json') -Raw | ConvertFrom-Json
if (-not $BuildSourceRoot) { $BuildSourceRoot=$aiRoot }
$aiNormalized=@()
$aiTextExtensions=@('.cs','.json','.meta','.asmdef','.shader','.uss','.uxml')
$aiUtf8=[Text.UTF8Encoding]::new($false,$true)
# AI adds no Unity sources. Verify that the supplied existing Player was built from these exact game sources.
foreach ($aiSource in $aiBuild.SourceFiles) {
    $aiSourcePath=Join-Path $aiRoot $aiSource.Path
    $aiBuiltSourcePath=Join-Path $BuildSourceRoot $aiSource.Path
    if ((Get-FileHash -LiteralPath $aiBuiltSourcePath).Hash.ToLowerInvariant() -ne $aiSource.Sha256) { throw "Player build source mismatch: $($aiSource.Path)" }
    if ((Get-FileHash -LiteralPath $aiSourcePath).Hash.ToLowerInvariant() -ne $aiSource.Sha256) {
        if ([IO.Path]::GetExtension($aiSourcePath) -notin $aiTextExtensions) { throw "Player binary source mismatch: $($aiSource.Path)" }
        $aiCurrentText=$aiUtf8.GetString([IO.File]::ReadAllBytes($aiSourcePath)).Replace("`r`n","`n")
        $aiBuiltText=$aiUtf8.GetString([IO.File]::ReadAllBytes($aiBuiltSourcePath)).Replace("`r`n","`n")
        if ($aiCurrentText -cne $aiBuiltText) { throw "Player source differs beyond line endings: $($aiSource.Path)" }
        $aiNormalized+=$aiSource.Path
    }
}
foreach ($aiFile in $aiBuild.Files) {
    $aiPayload=Join-Path (Split-Path -Parent $aiPlayer) $aiFile.Path
    if ((Get-FileHash -LiteralPath $aiPayload).Hash.ToLowerInvariant() -ne $aiFile.Sha256) { throw "Player payload mismatch: $($aiFile.Path)" }
}
$aiInput=Join-Path $aiEpisode 'scenario.json'
if (-not (Test-Path -LiteralPath $aiInput)) { throw 'No compatible scenario export (existing Unity scenario limit: 1000 commands). Use authoritative save verification instead.' }
$aiExpected=Get-Content -LiteralPath (Join-Path $aiEpisode 'result.json') -Raw | ConvertFrom-Json
if (-not $aiExpected.ScenarioVerified) { throw 'C# scenario replay has not passed.' }
if (-not $Output) { $Output=Join-Path $aiRoot ('artifacts/ai/unity-replay/'+[Guid]::NewGuid().ToString('N')) }
$Output=[IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Output must be a new directory.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$aiReport=Join-Path $Output 'report.json'
$aiArguments='-goaScenario "'+$aiInput+'" -goaScenarioReport "'+$aiReport+'" -goaSavePath "'+(Join-Path $Output 'manual-save.json')+'" -logFile "'+(Join-Path $Output 'player.log')+'"'
if ($Visual) { $aiArguments+=' -goaScenarioDelay 0.1 -goaScenarioQuit -screen-fullscreen 0 -screen-width 1280 -screen-height 720' }
else { $aiArguments+=' -batchmode -nographics' }
$aiStart=[DateTime]::UtcNow
$aiProcess=if ($Visual) { Start-Process -FilePath $aiPlayer -ArgumentList $aiArguments -PassThru } else { Start-Process -FilePath $aiPlayer -ArgumentList $aiArguments -WindowStyle Hidden -PassThru }
$aiEvidence=[ordered]@{ player=$aiPlayer; input=$aiInput; inputSha256=(Get-FileHash -LiteralPath $aiInput).Hash.ToLowerInvariant(); expectedHash=$aiExpected.StateHash; buildSourceRoot=$BuildSourceRoot; sourceFilesVerified=$aiBuild.SourceFiles.Count; payloadFilesVerified=$aiBuild.Files.Count; lineEndingOnlyDifferences=$aiNormalized; startedUtc=$aiStart; visual=[bool]$Visual; passed=$false; error='' }
try {
    while (-not $aiProcess.HasExited) {
        if (([DateTime]::UtcNow-$aiStart).TotalSeconds -gt $TimeoutSeconds) { $aiProcess.Kill(); throw 'Unity replay timeout.' }
        Start-Sleep -Milliseconds 250
    }
    $aiActual=Get-Content -LiteralPath $aiReport -Raw | ConvertFrom-Json
    if ($aiProcess.ExitCode -ne 0 -or -not $aiActual.Passed -or -not $aiActual.Complete -or $aiActual.FinalStateHash -ne $aiExpected.StateHash) { throw 'Unity replay failed or state differs.' }
    $aiEvidence.passed=$true
    Write-Output "Unity replay PASS: $($aiActual.TotalSteps) commands, $($aiActual.FinalStateHash)"
} catch { $aiEvidence.error=$_.Exception.Message; throw }
finally { $aiEvidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'verification.json') -Encoding utf8 }

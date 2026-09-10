param(
    [string[]]$Scenario = @(),
    [switch]$Visual,
    [switch]$KeepOpen,
    [switch]$Paused,
    [switch]$Build,
    [ValidateRange(0,10)][double]$Delay = 0.75,
    [ValidateRange(10,3600)][int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$goaRoot = Split-Path -Parent $PSScriptRoot
if (($KeepOpen -or $Paused) -and -not $Visual) { throw 'KeepOpen and Paused require Visual.' }
if ($Build) { & "$PSScriptRoot/build-unity.ps1" -Task BuildWindows }
$goaExecutable = Join-Path $goaRoot 'artifacts/player/Goa2V1.exe'
if (-not (Test-Path -LiteralPath $goaExecutable)) { throw 'Build the Player first, or pass -Build.' }
$goaFiles = if ($Scenario.Count) { @($Scenario | ForEach-Object { (Resolve-Path -LiteralPath $_).Path }) } else { @(Get-ChildItem -LiteralPath (Join-Path $goaRoot 'tests/scenarios') -Filter '*.json' -File | Sort-Object Name | Select-Object -ExpandProperty FullName) }
if ($goaFiles.Count -eq 0) { throw 'No scenario files found.' }
if ($Visual -and $goaFiles.Count -ne 1) { throw 'Choose one scenario with -Scenario when using -Visual.' }
if ($Visual) {
    $goaPidPath = Join-Path $goaRoot 'artifacts/unity/player.pid'
    if (Test-Path -LiteralPath $goaPidPath) {
        $goaExisting = Get-Process -Id ([int](Get-Content -LiteralPath $goaPidPath)) -ErrorAction SilentlyContinue
        if ($goaExisting -and $goaExisting.ProcessName -eq 'Goa2V1') { throw 'Close the existing visible Player before starting a visual scenario.' }
    }
}
foreach ($goaFile in $goaFiles) {
    $goaMode = if ($Visual) { 'visual' } else { 'headless' }
    $goaName = [System.IO.Path]::GetFileNameWithoutExtension($goaFile)
    $goaOutput = Join-Path $goaRoot ('artifacts/scenarios/'+$goaName+'/'+$goaMode+'/'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $goaOutput -Force | Out-Null
    $goaReportPath = Join-Path $goaOutput 'report.json'
    $goaLogPath = Join-Path $goaOutput 'player.log'
    $goaHashes = @{}
    foreach ($goaAssembly in @('Goa2.Domain.dll','Goa2.Rules.dll','Goa2.Application.dll','Goa2.Infrastructure.dll','Goa2.Presentation.dll')) {
        $goaHashes[$goaAssembly] = (Get-FileHash -LiteralPath (Join-Path $goaRoot ('artifacts/player/Goa2V1_Data/Managed/'+$goaAssembly))).Hash.ToLowerInvariant()
    }
    $goaArguments = '-goaScenario "'+$goaFile+'" -goaScenarioReport "'+$goaReportPath+'" -goaSavePath "'+(Join-Path $goaOutput 'manual-save.json')+'" -logFile "'+$goaLogPath+'"'
    if ($Visual) {
        $goaArguments += ' -screen-fullscreen 0 -screen-width 1600 -screen-height 1000 -goaScenarioDelay '+$Delay.ToString([Globalization.CultureInfo]::InvariantCulture)
        $goaArguments += ' -goaScreenshot "'+(Join-Path $goaRoot 'artifacts/unity/render-latest.png')+'"'
        if (-not $KeepOpen) { $goaArguments += ' -goaScenarioQuit' }
        if ($Paused) { $goaArguments += ' -goaScenarioPaused' }
        $goaProcess = Start-Process -FilePath $goaExecutable -ArgumentList $goaArguments -PassThru
        Set-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/player.pid') -Value $goaProcess.Id
    } else {
        $goaArguments += ' -batchmode -nographics'
        $goaProcess = Start-Process -FilePath $goaExecutable -ArgumentList $goaArguments -WindowStyle Hidden -PassThru
    }
    [pscustomobject]@{ mode=$goaMode; input=$goaFile; inputSha256=(Get-FileHash -LiteralPath $goaFile).Hash.ToLowerInvariant(); assemblies=$goaHashes; startedUtc=[DateTime]::UtcNow.ToString('o'); pid=$goaProcess.Id } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $goaOutput 'run.json') -Encoding utf8
    $goaDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $goaProcess.HasExited) {
        if ($KeepOpen -and (Test-Path -LiteralPath $goaReportPath)) { break }
        if ([DateTime]::UtcNow -ge $goaDeadline) {
            if (-not $Visual) { $goaProcess.Kill() }
            throw "Scenario timed out. Inspect $goaOutput"
        }
        Start-Sleep -Milliseconds 150
    }
    if (-not (Test-Path -LiteralPath $goaReportPath)) { throw "Player did not create a scenario report. Inspect $goaLogPath" }
    $goaReport = Get-Content -LiteralPath $goaReportPath -Raw | ConvertFrom-Json
    $goaRunPath = Join-Path $goaOutput 'run.json'
    $goaRun = Get-Content -LiteralPath $goaRunPath -Raw | ConvertFrom-Json
    $goaRun | Add-Member -NotePropertyName exitCode -NotePropertyValue $(if ($goaProcess.HasExited) { $goaProcess.ExitCode } else { $null })
    $goaRun | Add-Member -NotePropertyName finishedUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
    $goaRun | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $goaRunPath -Encoding utf8
    $goaLatest = Split-Path -Parent $goaOutput
    Copy-Item -LiteralPath $goaReportPath -Destination (Join-Path $goaLatest 'latest.json') -Force
    if (-not $goaReport.Complete -or -not $goaReport.Passed -or ($goaProcess.HasExited -and $goaProcess.ExitCode -ne 0)) { throw "Scenario failed: $goaName. Report: $goaReportPath" }
    Write-Output "PASS $goaName [$goaMode] $($goaReport.Steps.Count) steps; state $($goaReport.FinalStateHash)"
    Write-Output "Report: $goaReportPath"
}

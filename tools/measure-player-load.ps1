param(
    [Parameter(Mandatory=$true)][string]$SavePath,
    [string]$PlayerDirectory,
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,39}$')][string]$Label='load',
    [ValidateRange(1152,3840)][int]$Width=1280,
    [ValidateRange(768,2160)][int]$Height=800,
    [ValidateRange(1,600)][int]$BudgetSeconds=15,
    [ValidateRange(15,600)][int]$TimeoutSeconds=180
)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
if (-not $PlayerDirectory) { $PlayerDirectory=Join-Path $goaRoot 'artifacts/player' }
$goaPlayerDirectory=(Resolve-Path -LiteralPath $PlayerDirectory).Path
$goaExe=Join-Path $goaPlayerDirectory 'Goa2V1.exe'
if (-not (Test-Path -LiteralPath $goaExe)) { throw 'PlayerDirectory does not contain Goa2V1.exe.' }
if ($BudgetSeconds -gt $TimeoutSeconds) { throw 'BudgetSeconds must not exceed TimeoutSeconds.' }
$goaSave=(Resolve-Path -LiteralPath $SavePath).Path
$goaInitial=Get-Content -LiteralPath $goaSave -Raw | ConvertFrom-Json
$goaBatch=Join-Path $goaRoot ('artifacts/performance/'+$Label+'-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $goaBatch | Out-Null
$goaInput=Join-Path $goaBatch 'input-save.json'
Copy-Item -LiteralPath $goaSave -Destination $goaInput
$goaCapture=Join-Path $goaBatch 'loaded.png'
$goaSnapshot=Join-Path $goaBatch 'loaded.ui.json'
$goaSaveOutput=Join-Path $goaBatch 'manual-save.json'
$goaLog=Join-Path $goaBatch 'player.log'
$goaBuildInfo=Join-Path $goaPlayerDirectory 'build-info.json'
if (Test-Path -LiteralPath $goaBuildInfo) { Copy-Item -LiteralPath $goaBuildInfo -Destination (Join-Path $goaBatch 'build-info.json') }
$goaAssemblies=@(Get-ChildItem -LiteralPath (Join-Path $goaPlayerDirectory 'Goa2V1_Data/Managed') -Filter '*.dll' | Where-Object { $_.Name -like 'Goa2.*' -or $_.Name -eq 'Assembly-CSharp.dll' } | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$goaReport=[ordered]@{
    startedUtc=[DateTime]::UtcNow.ToString('o');finishedUtc=$null;label=$Label
    method='Visible native process launch to fresh QA snapshot after full save restoration; includes launch, first render and QA capture; no synthetic game commands'
    playerDirectory=$goaPlayerDirectory;inputSha256=(Get-FileHash -LiteralPath $goaInput -Algorithm SHA256).Hash.ToLowerInvariant()
    inputBytes=(Get-Item -LiteralPath $goaInput).Length;commands=@($goaInitial.AcceptedCommands).Count
    expectedRevision=$goaInitial.Revision;assemblies=$goaAssemblies;budgetSeconds=$BudgetSeconds
    ready=$false;withinBudget=$false;loadToQaSnapshotSeconds=$null;processCpuSeconds=$null;error=$null
}
$goaPlayer=$null
$goaWatch=[Diagnostics.Stopwatch]::new()
try {
    . "$PSScriptRoot/ui-qa-common.ps1"
    Close-QaPlayer
    $goaArguments='-screen-fullscreen 0 -screen-width '+$Width+' -screen-height '+$Height+
        ' -goaLoad "'+$goaInput+'" -goaScreenshot "'+$goaCapture+'" -goaSavePath "'+$goaSaveOutput+'" -logFile "'+$goaLog+'"'
    $goaWatch.Start()
    # This benchmark deliberately measures a visible interactive Player.
    $goaPlayer=Start-Process -FilePath $goaExe -WorkingDirectory $goaPlayerDirectory -ArgumentList $goaArguments -PassThru
    do {
        Start-Sleep -Milliseconds 100
        $goaPlayer.Refresh()
        if ($goaPlayer.HasExited) { throw "Player exited before a restored snapshot (exit $($goaPlayer.ExitCode))." }
        if (-not (Test-Path -LiteralPath $goaSnapshot)) { continue }
        try { $goaUi=Get-Content -LiteralPath $goaSnapshot -Raw | ConvertFrom-Json } catch { continue }
        if ($goaUi.Revision -eq $goaInitial.Revision -and $goaUi.Width -eq $Width -and $goaUi.Height -eq $Height) {
            $goaReport.ready=$true
            break
        }
    } while ($goaWatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    $goaWatch.Stop()
    $goaReport.loadToQaSnapshotSeconds=$goaWatch.Elapsed.TotalSeconds
    $goaPlayer.Refresh()
    $goaReport.processCpuSeconds=$goaPlayer.TotalProcessorTime.TotalSeconds
    $goaReport.withinBudget=$goaReport.ready -and $goaWatch.Elapsed.TotalSeconds -le $BudgetSeconds
    if (-not $goaReport.ready) { throw "No restored snapshot within $TimeoutSeconds seconds. Inspect player.log." }
    if (-not $goaReport.withinBudget) { throw "Load took $([math]::Round($goaWatch.Elapsed.TotalSeconds,2)) seconds, exceeding the $BudgetSeconds second budget." }
} catch {
    $goaReport.error=$_.Exception.Message
    throw
} finally {
    $goaWatch.Stop()
    if ($null -eq $goaReport.loadToQaSnapshotSeconds) { $goaReport.loadToQaSnapshotSeconds=$goaWatch.Elapsed.TotalSeconds }
    if ($null -ne $goaPlayer) {
        $goaPlayer.Refresh()
        if (-not $goaPlayer.HasExited) {
            [void]$goaPlayer.CloseMainWindow()
            if (-not $goaPlayer.WaitForExit(5000)) { Write-Warning "Benchmark Player PID $($goaPlayer.Id) is still open; close it before the next visual test." }
        }
    }
    $goaReport.finishedUtc=[DateTime]::UtcNow.ToString('o')
    $goaReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $goaBatch 'load.json') -Encoding utf8
    Write-Output "Load benchmark: $(Join-Path $goaBatch 'load.json')"
}

param([Parameter(Mandatory=$true)][string[]]$Scenario,[string]$UnityExe)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
if (-not $UnityExe) { $UnityExe=Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe' }
if (-not (Test-Path -LiteralPath $UnityExe)) { throw 'Provide -UnityExe for Unity 6000.3.23f1.' }
$goaBatchId=[Guid]::NewGuid().ToString('N')
$goaBatchRoot=Join-Path $goaRoot 'artifacts/editor-scenarios/batches'
New-Item -ItemType Directory -Path $goaBatchRoot -Force | Out-Null
$goaManifest=Join-Path $goaBatchRoot ($goaBatchId+'.json')
$goaLog=Join-Path $goaBatchRoot ($goaBatchId+'.log')
$goaEntries=@($Scenario | ForEach-Object {
    $goaInput=(Resolve-Path -LiteralPath $_).Path
    @{Scenario=$goaInput;Output=(Join-Path $goaRoot ('artifacts/editor-scenarios/'+[IO.Path]::GetFileNameWithoutExtension($goaInput)+'/'+[Guid]::NewGuid().ToString('N')))}
})
ConvertTo-Json -InputObject $goaEntries | Set-Content -LiteralPath $goaManifest -Encoding utf8
$goaArgs='-batchmode -nographics -projectPath "'+(Join-Path $goaRoot 'unity')+'" -executeMethod Goa2.Editor.ScenarioBatch.Run -goaScenarioBatch "'+$goaManifest+'" -logFile "'+$goaLog+'"'
$goaProcess=Start-Process -FilePath $UnityExe -ArgumentList $goaArgs -WindowStyle Hidden -PassThru
$goaProcess.WaitForExit()
if ($goaProcess.ExitCode -ne 0) { throw "Unity Editor scenario batch failed ($($goaProcess.ExitCode)). See $goaLog" }
foreach ($goaEntry in $goaEntries) {
    $goaReport=Get-Content -LiteralPath (Join-Path $goaEntry.Output 'report.json') -Raw | ConvertFrom-Json
    if (-not $goaReport.Passed -or -not $goaReport.Complete) { throw "Scenario failed: $($goaEntry.Scenario)" }
    Write-Output "PASS $($goaReport.Id) $($goaEntry.Output)"
}

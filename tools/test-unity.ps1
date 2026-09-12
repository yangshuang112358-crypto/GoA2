param(
    [string]$UnityExe,
    [ValidatePattern('^[A-Za-z0-9_.;]*$')][string]$Filter,
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$ReportName = 'core-editmode',
    [ValidatePattern('^[A-Za-z0-9_.;]+$')][string]$Assemblies = 'Goa2.Core.Tests'
)
$ErrorActionPreference = 'Stop'
$goaRoot = Split-Path -Parent $PSScriptRoot
if (-not $UnityExe) {
    $goaCandidates = @(
        (Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe'),
        (Join-Path $env:ProgramFiles 'Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe'),
        (Join-Path $env:ProgramFiles 'Unity 6000.3.23f1/Editor/Unity.exe')
    )
    $UnityExe = $goaCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $UnityExe) { throw 'Unity 6000.3.23f1 was not found. Provide -UnityExe.' }
$goaResults = Join-Path $goaRoot 'artifacts/unity/tests'
New-Item -ItemType Directory -Path $goaResults -Force | Out-Null
$goaUniqueReport = Join-Path $goaResults ($ReportName+'-'+[Guid]::NewGuid().ToString('N')+'.xml')
$goaLog = Join-Path $goaResults ($ReportName+'.log')
# Unity's test runner owns process exit; -quit would terminate before tests finish.
$goaArguments = '-batchmode -nographics -projectPath "'+(Join-Path $goaRoot 'unity')+'" -runTests -testPlatform EditMode -assemblyNames "'+$Assemblies+'" -testResults "'+$goaUniqueReport+'" -logFile "'+$goaLog+'"'
if ($Filter) { $goaArguments += ' -testFilter "'+$Filter+'"' }
$goaProcess = Start-Process -FilePath $UnityExe -ArgumentList $goaArguments -WindowStyle Hidden -PassThru
$goaProcess.WaitForExit()
if (-not (Test-Path -LiteralPath $goaUniqueReport)) { throw "Unity did not produce a new test report (exit $($goaProcess.ExitCode)). See $goaLog" }
$goaStableReport = Join-Path $goaResults ($ReportName+'.xml')
Copy-Item -LiteralPath $goaUniqueReport -Destination $goaStableReport -Force
[xml]$goaXml = Get-Content -LiteralPath $goaUniqueReport -Raw
$goaRun = $goaXml.'test-run'
if ($goaProcess.ExitCode -ne 0 -or [int]$goaRun.total -eq 0 -or $goaRun.result -ne 'Passed' -or [int]$goaRun.failed -ne 0) {
    throw "Unity tests failed or were empty: $($goaRun.passed)/$($goaRun.total), failed $($goaRun.failed). See $goaStableReport"
}
Write-Output "Unity EditMode passed: $($goaRun.passed)/$($goaRun.total). Report: $goaStableReport"

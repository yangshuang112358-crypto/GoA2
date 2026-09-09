param([string]$UnityExe, [ValidateSet("Prepare","Validate","BuildWindows")][string]$Task = "BuildWindows")
$ErrorActionPreference = "Stop"
$goaRoot = Split-Path -Parent $PSScriptRoot
$goaVersion = "6000.3.23f1"
if (-not $UnityExe) {
    $goaCandidates = @(
        (Join-Path $env:USERPROFILE "UnityEditors\$goaVersion\Editor\Unity.exe"),
        (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$goaVersion\Editor\Unity.exe"),
        (Join-Path $env:ProgramFiles "Unity $goaVersion\Editor\Unity.exe")
    )
    $UnityExe = $goaCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $UnityExe) { throw "Unity $goaVersion was not found. Provide -UnityExe." }
$goaLogDirectory = Join-Path $goaRoot "artifacts\unity"
New-Item -ItemType Directory -Force -Path $goaLogDirectory | Out-Null
$goaLog = Join-Path $goaLogDirectory ($Task + ".log")
$goaProject = Join-Path $goaRoot "unity"
$goaArguments = '-batchmode -nographics -quit -projectPath "' + $goaProject + '" -executeMethod Goa2.Editor.BuildTools.' + $Task + ' -logFile "' + $goaLog + '"'
$goaProcess = Start-Process -FilePath $UnityExe -ArgumentList $goaArguments -WindowStyle Hidden -PassThru
$goaProcess.WaitForExit()
if ($goaProcess.ExitCode -ne 0) { throw "Unity failed ($($goaProcess.ExitCode)). Inspect $goaLog" }
$goaMarker = switch ($Task) { 'Prepare' { 'GOA2_PREPARE_OK' } 'Validate' { 'GOA2_EDITOR_VALIDATION_PASS' } default { 'GOA2_BUILD_PASS' } }
if (-not (Select-String -LiteralPath $goaLog -SimpleMatch $goaMarker -Quiet)) { throw "Unity did not finish the requested task. Inspect $goaLog" }
Write-Output "Unity $Task passed. Log: $goaLog"

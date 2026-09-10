param(
    [switch]$Qa,
    [ValidateRange(1152,3840)][int]$Width = 1600,
    [ValidateRange(768,2160)][int]$Height = 1000,
    [string]$LoadSave
)
$ErrorActionPreference = 'Stop'
$goaRoot = Split-Path -Parent $PSScriptRoot
$goaExecutable = Join-Path $goaRoot 'artifacts/player/Goa2V1.exe'
if (-not (Test-Path -LiteralPath $goaExecutable)) { throw 'Build the Player using tools/build-unity.ps1 first.' }
$goaPidPath = Join-Path $goaRoot 'artifacts/unity/player.pid'
if (Test-Path -LiteralPath $goaPidPath) {
    $goaExisting = Get-Process -Id ([int](Get-Content -LiteralPath $goaPidPath)) -ErrorAction SilentlyContinue
    if ($goaExisting -and $goaExisting.ProcessName -eq 'Goa2V1') { throw 'A tracked player is already open. Close it before launching another.' }
}
$goaArguments = '-screen-fullscreen 0 -screen-width ' + $Width + ' -screen-height ' + $Height
$goaArguments += ' -logFile "' + (Join-Path $goaRoot 'artifacts/unity/player.log') + '"'
if ($Qa) {
    # A new process must publish its own layout; stale snapshots can otherwise satisfy readiness checks.
    $goaOldLayout = Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json'
    if (Test-Path -LiteralPath $goaOldLayout) { Remove-Item -LiteralPath $goaOldLayout }
    $goaArguments += ' -goaScreenshot "' + (Join-Path $goaRoot 'artifacts/unity/render-latest.png') + '"'
    $goaArguments += ' -goaSavePath "' + (Join-Path $goaRoot 'artifacts/unity/qa-save.json') + '"'
}
if ($LoadSave) {
    $goaLoadPath = (Resolve-Path -LiteralPath $LoadSave).Path
    $goaArguments += ' -goaLoad "' + $goaLoadPath + '"'
}
# This is an interactive game window; it must be visible for manual testing.
$goaPlayer = Start-Process -FilePath $goaExecutable -ArgumentList $goaArguments -PassThru
Set-Content -LiteralPath $goaPidPath -Value $goaPlayer.Id
Write-Output "Player started (PID $($goaPlayer.Id), ${Width}x${Height}, QA=$Qa)."

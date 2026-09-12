param([ValidateRange(1280,3840)][int]$Width=1600,[ValidateRange(800,2160)][int]$Height=1000,[switch]$Visual,[switch]$Editor,[string]$UnityExe)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaOutput=Join-Path $goaRoot ('artifacts/card-layout/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $goaOutput -Force | Out-Null
$goaReport=Join-Path $goaOutput 'report.json'
$goaArgs='-screen-fullscreen 0 -screen-width '+$Width+' -screen-height '+$Height+' -goaScreenshot "'+(Join-Path $goaOutput 'layout.png')+'" -goaSavePath "'+(Join-Path $goaOutput 'qa-save.json')+'" -goaCardReadingAudit "'+$goaReport+'" -logFile "'+(Join-Path $goaOutput 'player.log')+'"'
$goaWindowStyle=if($Visual) {'Normal'} else {'Hidden'}
$goaExecutable=Join-Path $goaRoot 'artifacts/player/Goa2V1.exe'
if ($Editor) {
    if (-not $UnityExe) { $UnityExe=Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe' }
    $goaExecutable=$UnityExe
    $goaArgs='-batchmode -projectPath "'+(Join-Path $goaRoot 'unity')+'" -executeMethod Goa2.Editor.RevealedStripeAuditRunner.Run -goaCardReadingEditor '+$goaArgs
}
$goaProcess=Start-Process -FilePath $goaExecutable -ArgumentList $goaArgs -WindowStyle $goaWindowStyle -PassThru
if (-not $goaProcess.WaitForExit(120000)) { Stop-Process -Id $goaProcess.Id;throw "Layout audit timed out: $goaOutput" }
if (-not (Test-Path -LiteralPath $goaReport)) { throw "No layout report: $goaOutput" }
$goaResult=Get-Content -LiteralPath $goaReport -Raw | ConvertFrom-Json
if ($goaProcess.ExitCode -ne 0 -or -not $goaResult.Passed) { throw "Card layout failed: $goaReport" }
if ($goaResult.Width -ne $Width -or $goaResult.Height -ne $Height) { throw "Unexpected audit viewport: $goaReport" }
Write-Output "PASS 108 full card layouts and glossary at $Width x $Height. Mode: $($goaResult.Mode). Report: $goaReport"

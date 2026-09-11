param([ValidateRange(1280,3840)][int]$Width=1600,[ValidateRange(800,2160)][int]$Height=1000,[switch]$Visual)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaOutput=Join-Path $goaRoot ('artifacts/card-layout/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $goaOutput -Force | Out-Null
$goaReport=Join-Path $goaOutput 'report.json'
$goaArgs='-screen-fullscreen 0 -screen-width '+$Width+' -screen-height '+$Height+' -goaScreenshot "'+(Join-Path $goaOutput 'layout.png')+'" -goaSavePath "'+(Join-Path $goaOutput 'qa-save.json')+'" -goaCardReadingAudit "'+$goaReport+'" -logFile "'+(Join-Path $goaOutput 'player.log')+'"'
$goaWindowStyle=if($Visual) {'Normal'} else {'Hidden'}
$goaProcess=Start-Process -FilePath (Join-Path $goaRoot 'artifacts/player/Goa2V1.exe') -ArgumentList $goaArgs -WindowStyle $goaWindowStyle -PassThru
if (-not $goaProcess.WaitForExit(120000)) { Stop-Process -Id $goaProcess.Id;throw "Layout audit timed out: $goaOutput" }
if (-not (Test-Path -LiteralPath $goaReport)) { throw "No layout report: $goaOutput" }
$goaResult=Get-Content -LiteralPath $goaReport -Raw | ConvertFrom-Json
if ($goaProcess.ExitCode -ne 0 -or -not $goaResult.Passed) { throw "Card layout failed: $goaReport" }
Write-Output "PASS 108 full card layouts at $Width x $Height. Report: $goaReport"

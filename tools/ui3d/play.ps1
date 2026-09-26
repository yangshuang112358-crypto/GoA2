param([switch]$Fallback2D,[switch]$Qa,[int]$Width=1600,[int]$Height=1000)
$ErrorActionPreference='Stop'
$uiRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$uiPlayer=Join-Path $uiRoot 'artifacts/player/Goa2V1.exe'
if(-not (Test-Path -LiteralPath $uiPlayer)) {throw 'Build first: ./tools/build-unity.ps1'}
$uiOutput=Join-Path $uiRoot 'artifacts/ui3d/player'
New-Item -ItemType Directory -Force $uiOutput | Out-Null
$uiArgs='-screen-fullscreen 0 -screen-width '+$Width+' -screen-height '+$Height+' -goaSavePath "'+(Join-Path $uiOutput 'hotseat.json')+'" -logFile "'+(Join-Path $uiOutput 'player.log')+'"'
if($Fallback2D) {$uiArgs+=' -goa2d'}
if($Qa) {$uiArgs+=' -goaScreenshot "'+(Join-Path $uiOutput 'render.png')+'"'}
# The requested game is an interactive visible window; not a background helper.
$uiProcess=Start-Process -FilePath $uiPlayer -ArgumentList $uiArgs -PassThru
Set-Content -LiteralPath (Join-Path $uiOutput 'player.pid') -Value $uiProcess.Id
Write-Output "UI3D Player PID $($uiProcess.Id); isolated saves/logs: $uiOutput"

param([int]$Width=1600,[int]$Height=1000)
$ErrorActionPreference='Stop'
$tutorialRoot=Split-Path -Parent $PSScriptRoot
$tutorialPlayer=Join-Path $tutorialRoot 'artifacts/player/Goa2V1.exe'
if(-not (Test-Path -LiteralPath $tutorialPlayer -PathType Leaf)){throw '请先运行 tools/build-unity.ps1 生成游戏客户端。'}
$tutorialLogs=Join-Path $tutorialRoot 'artifacts/tutorial/logs'
New-Item -ItemType Directory -Path $tutorialLogs -Force | Out-Null
$tutorialLog=Join-Path $tutorialLogs ('player-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'.log')
$tutorialArgs='-goaTutorial -screen-fullscreen 0 -screen-width '+$Width+' -screen-height '+$Height+' -logFile "'+$tutorialLog+'"'
# The user is explicitly opening an interactive tutorial window.
Start-Process -FilePath $tutorialPlayer -ArgumentList $tutorialArgs -WorkingDirectory (Split-Path -Parent $tutorialPlayer) | Out-Null

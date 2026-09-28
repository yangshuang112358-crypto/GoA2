param([string]$Ffmpeg,[int]$Width=1600,[int]$Height=1000)
$ErrorActionPreference='Stop'
$demoRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if(-not $Ffmpeg){$Ffmpeg=Join-Path $demoRoot 'artifacts/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'}
if(-not (Test-Path -LiteralPath $Ffmpeg)){throw 'Supply -Ffmpeg with a local FFmpeg executable.'}
$demoSource=Join-Path $demoRoot 'unity/Assets/Scripts/UI3D/GameScreen.VideoDemo.cs'
if(Test-Path -LiteralPath $demoSource){throw 'Another recording source is already installed; do not overwrite it.'}
$demoOutput=Join-Path $demoRoot ('artifacts/videos/terrain-'+$Width+'x'+$Height+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $demoOutput | Out-Null
try {
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameScreen.TerrainDemo.cs.txt') -Destination $demoSource
 $demoArgs='-batchmode -projectPath "'+(Join-Path $demoRoot 'unity')+'" -executeMethod Goa2.UI3D.Editor.Board3DAuditRunner.Run -goaCardReadingEditor -screen-width '+$Width+' -screen-height '+$Height+' -goaVideoDemo "'+$demoOutput+'" -goaVideoFfmpeg "'+$Ffmpeg+'" -logFile "'+(Join-Path $demoOutput 'editor.log')+'"'
 $demoProcess=Start-Process -FilePath (Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe') -ArgumentList $demoArgs -WindowStyle Hidden -PassThru
 $demoProcess.WaitForExit()
 if($demoProcess.ExitCode -ne 0){throw "Recording failed: $demoOutput"}
 $demoResult=Join-Path $demoOutput 'capture-result.txt'
 if(-not (Test-Path -LiteralPath $demoResult) -or -not ((Get-Content -LiteralPath $demoResult -Raw).StartsWith('PASS'))){throw "No successful capture report: $demoOutput"}
 Write-Output "Recorded: $demoOutput"
} finally {
 Remove-Item -LiteralPath $demoSource -ErrorAction SilentlyContinue
 Remove-Item -LiteralPath ($demoSource+'.meta') -ErrorAction SilentlyContinue
}

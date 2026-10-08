param([int]$Width=1600,[int]$Height=1000)
$ErrorActionPreference='Stop'
$goaMotionRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$goaMotionSource=Join-Path $goaMotionRoot 'unity/Assets/Scripts/UI3D/GameScreen.VideoDemo.cs'
if(Test-Path -LiteralPath $goaMotionSource){throw 'A recording source already exists; preserve it.'}
$goaMotionUnity=Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe'
$goaMotionFfmpeg=Join-Path $goaMotionRoot 'artifacts/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
foreach($goaMotionTool in @($goaMotionUnity,$goaMotionFfmpeg)){if(-not (Test-Path -LiteralPath $goaMotionTool)){throw "Missing recording tool: $goaMotionTool"}}
$goaMotionOutput=Join-Path $goaMotionRoot ('artifacts/videos/minion-motion-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $goaMotionOutput | Out-Null
try {
 $goaMotionTemplate=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'GameScreen.TerrainDemo.cs.txt') -Raw
 $goaMotionBoundary=$goaMotionTemplate.IndexOf('  private IEnumerator VideoSequence(){',[StringComparison]::Ordinal)
 if($goaMotionBoundary -lt 0){throw 'Recording prefix boundary was not found.'}
 $goaMotionPrefix=$goaMotionTemplate.Substring(0,$goaMotionBoundary)
 # Reapply photography-only chrome suppression if a real command rebuilds the UI mid-shot.
 $goaMotionOverlay='   demoLabel.text=demoCaption;'
 if(-not $goaMotionPrefix.Contains($goaMotionOverlay)){throw 'Recording overlay hook was not found.'}
 $goaMotionPrefix=$goaMotionPrefix.Replace($goaMotionOverlay,$goaMotionOverlay+'HideMotionChrome();')
 [IO.File]::WriteAllText($goaMotionSource,$goaMotionPrefix+(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'GameScreen.MinionMotionDemo.cs.txt') -Raw))
 $goaMotionArgs='-batchmode -projectPath "'+(Join-Path $goaMotionRoot 'unity')+'" -executeMethod Goa2.UI3D.Editor.Board3DAuditRunner.Run -goaCardReadingEditor -screen-width '+$Width+' -screen-height '+$Height+' -goaVideoDemo "'+$goaMotionOutput+'" -goaVideoFfmpeg "'+$goaMotionFfmpeg+'" -logFile "'+(Join-Path $goaMotionOutput 'editor.log')+'"'
 $goaMotionProcess=Start-Process -FilePath $goaMotionUnity -ArgumentList $goaMotionArgs -WindowStyle Hidden -PassThru
 $goaMotionProcess.WaitForExit()
 $goaMotionResult=Join-Path $goaMotionOutput 'capture-result.txt'
 if($goaMotionProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $goaMotionResult) -or -not ((Get-Content -LiteralPath $goaMotionResult -Raw).StartsWith('PASS'))){throw "Minion recording failed: $goaMotionOutput"}
 foreach($goaMotionClip in @('01-archer-motion-raw.mp4','02-combat-and-guards-raw.mp4')){
  $goaMotionClipPath=Join-Path $goaMotionOutput $goaMotionClip
  if(-not (Test-Path -LiteralPath $goaMotionClipPath) -or (Get-Item -LiteralPath $goaMotionClipPath).Length -lt 1024){throw "Missing or empty recording: $goaMotionClipPath"}
 }
 Write-Output "Unity rendered minion motion preview (scripted, silent): $goaMotionOutput"
} finally {
 Remove-Item -LiteralPath $goaMotionSource -ErrorAction SilentlyContinue
 Remove-Item -LiteralPath ($goaMotionSource+'.meta') -ErrorAction SilentlyContinue
}

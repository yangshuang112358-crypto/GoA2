param([int]$Width=1280,[int]$Height=720,[switch]$UpgradeOnly,[switch]$RevisionOnly)
$ErrorActionPreference='Stop'
$demoRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$demoSource=Join-Path $demoRoot 'unity/Assets/Scripts/UI3D/GameScreen.VideoDemo.cs'
if(Test-Path -LiteralPath $demoSource){throw 'A recording source already exists.'}
$demoOutput=Join-Path $demoRoot ('artifacts/videos/feedback-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
$demoFfmpeg=Join-Path $demoRoot 'artifacts/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
New-Item -ItemType Directory -Force -Path $demoOutput | Out-Null
try {
 $demoPrefix=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'GameScreen.TerrainDemo.cs.txt') -Raw).Split('  private IEnumerator VideoSequence(){')[0]
 $demoPrefix=$demoPrefix.Replace('var demo=VideoSequence();','var demo=FlattenDemo(VideoSequence());')
 if($UpgradeOnly){$demoPrefix=$demoPrefix.Replace('demoLabel.text=demoCaption;','demoLabel.text=demoCaption;demoLabel.style.left=20;demoLabel.style.right=StyleKeyword.Auto;demoLabel.style.bottom=StyleKeyword.Auto;demoLabel.style.top=70;demoLabel.style.width=240;demoLabel.style.height=110;demoLabel.style.fontSize=20;demoLabel.style.whiteSpace=WhiteSpace.Normal;')}
 $demoSuffix=if($RevisionOnly){'GameScreen.RevisionDemo.cs.txt'}elseif($UpgradeOnly){'GameScreen.UpgradeDemo.cs.txt'}else{'GameScreen.FeedbackDemo.cs.txt'}
 $demoText=$demoPrefix+(Get-Content -LiteralPath (Join-Path $PSScriptRoot $demoSuffix) -Raw)
 [IO.File]::WriteAllText($demoSource,$demoText)
 $demoArgs='-batchmode -projectPath "'+(Join-Path $demoRoot 'unity')+'" -executeMethod Goa2.UI3D.Editor.Board3DAuditRunner.Run -goaCardReadingEditor -screen-width '+$Width+' -screen-height '+$Height+' -goaVideoDemo "'+$demoOutput+'" -goaVideoFfmpeg "'+$demoFfmpeg+'" -logFile "'+(Join-Path $demoOutput 'editor.log')+'"'
 $demoProcess=Start-Process -FilePath (Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe') -ArgumentList $demoArgs -WindowStyle Hidden -PassThru
 $demoProcess.WaitForExit()
 if($demoProcess.ExitCode -ne 0){throw "Recording failed: $demoOutput"}
 $demoResult=Join-Path $demoOutput 'capture-result.txt'
 if(-not (Test-Path -LiteralPath $demoResult) -or -not ((Get-Content -LiteralPath $demoResult -Raw).StartsWith('PASS'))){throw "No successful capture report: $demoOutput"}
 Write-Output "Recorded genuine Unity frames, scripted controls, silent clips: $demoOutput"
} finally {
 Remove-Item -LiteralPath $demoSource -ErrorAction SilentlyContinue
 Remove-Item -LiteralPath ($demoSource+'.meta') -ErrorAction SilentlyContinue
}

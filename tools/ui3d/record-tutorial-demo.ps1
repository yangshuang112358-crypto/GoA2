param([int]$Width=1280,[int]$Height=720)
$ErrorActionPreference='Stop'
$tutorialRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tutorialSource=Join-Path $tutorialRoot 'unity/Assets/Scripts/UI3D/GameScreen.VideoDemo.cs'
if(Test-Path -LiteralPath $tutorialSource){throw 'A recording source already exists.'}
$tutorialOutput=Join-Path $tutorialRoot ('artifacts/videos/tutorial-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
$tutorialFfmpeg=Join-Path $tutorialRoot 'artifacts/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
New-Item -ItemType Directory -Force -Path $tutorialOutput | Out-Null
try {
 $tutorialPrefix=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'GameScreen.TerrainDemo.cs.txt') -Raw).Split('  private IEnumerator VideoSequence(){')[0]
 [IO.File]::WriteAllText($tutorialSource,$tutorialPrefix+(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'GameScreen.TutorialDemo.cs.txt') -Raw))
 $tutorialArgs='-batchmode -projectPath "'+(Join-Path $tutorialRoot 'unity')+'" -executeMethod Goa2.UI3D.Editor.Board3DAuditRunner.Run -goaCardReadingEditor -screen-width '+$Width+' -screen-height '+$Height+' -goaTutorialProgress "'+(Join-Path $tutorialOutput 'progress.json')+'" -goaVideoDemo "'+$tutorialOutput+'" -goaVideoFfmpeg "'+$tutorialFfmpeg+'" -logFile "'+(Join-Path $tutorialOutput 'editor.log')+'"'
 $tutorialProcess=Start-Process -FilePath (Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe') -ArgumentList $tutorialArgs -WindowStyle Hidden -PassThru
 $tutorialProcess.WaitForExit()
 $result=Join-Path $tutorialOutput 'capture-result.txt'
 if($tutorialProcess.ExitCode -ne 0 -or -not (Test-Path $result) -or -not ((Get-Content $result -Raw).StartsWith('PASS'))){throw "Recording failed: $tutorialOutput"}
 Write-Output "Unity rendered tutorial clip (scripted UI, silent): $tutorialOutput"
} finally {
 Remove-Item -LiteralPath $tutorialSource -ErrorAction SilentlyContinue
 Remove-Item -LiteralPath ($tutorialSource+'.meta') -ErrorAction SilentlyContinue
}

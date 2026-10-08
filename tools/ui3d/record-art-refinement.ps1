param([int]$Width=1600,[int]$Height=1000)
$ErrorActionPreference='Stop'
$goaArtRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$goaArtSource=Join-Path $goaArtRoot 'unity/Assets/Scripts/UI3D/GameScreen.VideoDemo.cs'
if(Test-Path -LiteralPath $goaArtSource){throw 'A recording source already exists; preserve it.'}
$goaArtOutput=Join-Path $goaArtRoot ('artifacts/videos/art-refinement-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $goaArtOutput | Out-Null
try {
 $goaArtPrefix=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'GameScreen.TerrainDemo.cs.txt') -Raw).Split('  private IEnumerator VideoSequence(){')[0]
 [IO.File]::WriteAllText($goaArtSource,$goaArtPrefix+(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'GameScreen.ArtRefinementDemo.cs.txt') -Raw))
 $goaArtFfmpeg=Join-Path $goaArtRoot 'artifacts/video-tools/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
 $goaArtArgs='-batchmode -projectPath "'+(Join-Path $goaArtRoot 'unity')+'" -executeMethod Goa2.UI3D.Editor.Board3DAuditRunner.Run -goaCardReadingEditor -screen-width '+$Width+' -screen-height '+$Height+' -goaVideoDemo "'+$goaArtOutput+'" -goaVideoFfmpeg "'+$goaArtFfmpeg+'" -logFile "'+(Join-Path $goaArtOutput 'editor.log')+'"'
 $goaArtProcess=Start-Process -FilePath (Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe') -ArgumentList $goaArtArgs -WindowStyle Hidden -PassThru
 $goaArtProcess.WaitForExit()
 $goaArtResult=Join-Path $goaArtOutput 'capture-result.txt'
 if($goaArtProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $goaArtResult) -or -not ((Get-Content -LiteralPath $goaArtResult -Raw).StartsWith('PASS'))){throw "Art recording failed: $goaArtOutput"}
 Write-Output "Unity rendered art preview (scripted UI, silent): $goaArtOutput"
} finally {
 Remove-Item -LiteralPath $goaArtSource -ErrorAction SilentlyContinue
 Remove-Item -LiteralPath ($goaArtSource+'.meta') -ErrorAction SilentlyContinue
}

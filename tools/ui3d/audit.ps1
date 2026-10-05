param([int]$Width=1600,[int]$Height=1000,[switch]$SettingsOnly,[switch]$SkillBadgesOnly,[switch]$TerrainOnly,[switch]$BattlefieldOnly,[switch]$ActionSequenceOnly,[switch]$WorldDecisionsOnly,[switch]$OpeningOnly,[switch]$CombatPresentationOnly,[switch]$Player)
$ErrorActionPreference='Stop'
$uiRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$uiUnity=Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe'
$uiOutput=Join-Path $uiRoot ('artifacts/ui3d/audit-'+$Width+'x'+$Height+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $uiOutput | Out-Null
$uiArgs='-batchmode -projectPath "'+(Join-Path $uiRoot 'unity')+'" -executeMethod Goa2.UI3D.Editor.Board3DAuditRunner.Run -goaCardReadingEditor -screen-width '+$Width+' -screen-height '+$Height+' -goa3dAudit "'+$uiOutput+'" -logFile "'+(Join-Path $uiOutput 'editor.log')+'"'
if($Player) {
 $uiUnity=Join-Path $uiRoot 'artifacts/player/Goa2V1.exe'
 $uiArgs='-screen-fullscreen 0 -screen-width '+$Width+' -screen-height '+$Height+' -goa3dAudit "'+$uiOutput+'" -goaAuditScenarios "'+(Join-Path $uiRoot 'tests/scenarios')+'" -goaSavePath "'+(Join-Path $uiOutput 'isolated-save.json')+'" -logFile "'+(Join-Path $uiOutput 'player.log')+'"'
}
if($BattlefieldOnly) {$uiArgs+=' -goaBattlefieldAuditOnly'}
if($OpeningOnly) {$uiArgs+=' -goaOpeningAuditOnly'}
if($CombatPresentationOnly) {$uiArgs+=' -goaCombatPresentationAuditOnly'}
if($WorldDecisionsOnly) {$uiArgs+=' -goaWorldDecisionsAuditOnly'}
if($ActionSequenceOnly) {$uiArgs+=' -goaActionSequenceAuditOnly'}
if($TerrainOnly) {$uiArgs+=' -goaTerrainAuditOnly'}
if($SettingsOnly) {$uiArgs+=' -goaSettingsAuditOnly'}
if($SkillBadgesOnly) {$uiArgs+=' -goaSkillBadgesAuditOnly'}
# A Player graphic audit must display its test window: Hidden can produce black captures.
if($Player) {$uiProcess=Start-Process -FilePath $uiUnity -ArgumentList $uiArgs -PassThru}
else {$uiProcess=Start-Process -FilePath $uiUnity -ArgumentList $uiArgs -WindowStyle Hidden -PassThru}
$uiProcess.WaitForExit()
$uiReport=Join-Path $uiOutput 'report.json'
if(-not (Test-Path -LiteralPath $uiReport)) {throw "No audit report; inspect $uiOutput"}
$uiResult=Get-Content -LiteralPath $uiReport -Raw | ConvertFrom-Json
if($uiProcess.ExitCode -ne 0 -or -not $uiResult.Passed) {throw "Audit failed; inspect $uiReport"}
Add-Type -AssemblyName System.Drawing
$uiImages=@(Get-ChildItem -LiteralPath $uiOutput -Filter '*.png' -File)
$uiCaptureChecks=@()
foreach($uiImage in $uiImages) {
 $uiBitmap=[System.Drawing.Bitmap]::FromFile($uiImage.FullName)
 try {
  $uiLow=765;$uiHigh=0
  for($uiY=0;$uiY -lt $uiBitmap.Height;$uiY+=[Math]::Max(1,[int]($uiBitmap.Height/32))) {
   for($uiX=0;$uiX -lt $uiBitmap.Width;$uiX+=[Math]::Max(1,[int]($uiBitmap.Width/32))) {
    $uiPixel=$uiBitmap.GetPixel($uiX,$uiY);$uiSum=[int]$uiPixel.R+[int]$uiPixel.G+[int]$uiPixel.B
    $uiLow=[Math]::Min($uiLow,$uiSum);$uiHigh=[Math]::Max($uiHigh,$uiSum)
   }
  }
  $uiCaptureChecks+=@{file=$uiImage.Name;passed=($uiHigh-$uiLow -gt 8);variation=($uiHigh-$uiLow)}
 } finally {$uiBitmap.Dispose()}
}
$uiCaptureChecks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $uiOutput 'capture-check.json') -Encoding utf8
if($uiImages.Count -eq 0 -or @($uiCaptureChecks | Where-Object {-not $_.passed}).Count -gt 0) {throw "Blank or missing capture; layout checks alone are not visual evidence: $uiOutput"}
Write-Output "Rendered synthetic audit passed: $uiReport"

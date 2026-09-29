param([int]$Width=1600,[int]$Height=1000,[switch]$SettingsOnly,[switch]$SkillBadgesOnly,[switch]$TerrainOnly,[switch]$BattlefieldOnly,[switch]$ActionSequenceOnly)
$ErrorActionPreference='Stop'
$uiRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$uiUnity=Join-Path $env:USERPROFILE 'UnityEditors/6000.3.23f1/Editor/Unity.exe'
$uiOutput=Join-Path $uiRoot ('artifacts/ui3d/audit-'+$Width+'x'+$Height+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $uiOutput | Out-Null
$uiArgs='-batchmode -projectPath "'+(Join-Path $uiRoot 'unity')+'" -executeMethod Goa2.UI3D.Editor.Board3DAuditRunner.Run -goaCardReadingEditor -screen-width '+$Width+' -screen-height '+$Height+' -goa3dAudit "'+$uiOutput+'" -logFile "'+(Join-Path $uiOutput 'editor.log')+'"'
if($BattlefieldOnly) {$uiArgs+=' -goaBattlefieldAuditOnly'}
if($ActionSequenceOnly) {$uiArgs+=' -goaActionSequenceAuditOnly'}
if($TerrainOnly) {$uiArgs+=' -goaTerrainAuditOnly'}
if($SettingsOnly) {$uiArgs+=' -goaSettingsAuditOnly'}
if($SkillBadgesOnly) {$uiArgs+=' -goaSkillBadgesAuditOnly'}
$uiProcess=Start-Process -FilePath $uiUnity -ArgumentList $uiArgs -WindowStyle Hidden -PassThru
$uiProcess.WaitForExit()
$uiReport=Join-Path $uiOutput 'report.json'
if(-not (Test-Path -LiteralPath $uiReport)) {throw "No audit report; inspect $uiOutput"}
$uiResult=Get-Content -LiteralPath $uiReport -Raw | ConvertFrom-Json
if($uiProcess.ExitCode -ne 0 -or -not $uiResult.Passed) {throw "Audit failed; inspect $uiReport"}
Write-Output "Rendered synthetic audit passed: $uiReport"

param(
    [ValidateSet('Init','Check','Export','Open')][string]$Mode='Check',
    [string]$Asset='',
    [string]$BlenderExe=''
)
$ErrorActionPreference='Stop'
$goaRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $BlenderExe) { $BlenderExe=Join-Path $env:USERPROFILE 'Applications/Blender/blender-4.5.14-windows-x64/blender.exe' }
if (-not (Test-Path -LiteralPath $BlenderExe -PathType Leaf)) { throw 'Blender 4.5.14 missing; pass -BlenderExe with its full path.' }
$goaConfig=Join-Path $goaRoot 'artifacts/blender-user-config'
New-Item -ItemType Directory -Path $goaConfig -Force | Out-Null
$goaOldConfig=$env:BLENDER_USER_CONFIG
try {
    $env:BLENDER_USER_CONFIG=$goaConfig
    if ($Mode -eq 'Open') {
        if (-not $Asset) { throw 'Use -Asset with a working .blend copy; do not edit the template directly.' }
        $goaAsset=(Resolve-Path -LiteralPath $Asset).Path
        # User-requested interactive editor; opening a visible window is intentional.
        Start-Process -FilePath $BlenderExe -ArgumentList @('--disable-autoexec',('"'+$goaAsset+'"')) | Out-Null
        return
    }
    $goaOutput=Join-Path $goaRoot ('artifacts/art-production/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
    $goaArgs=@('--background','--factory-startup','--disable-autoexec','--python-exit-code','1','--python',(Join-Path $PSScriptRoot 'production.py'),'--','--root',$goaRoot,'--mode',$Mode.ToLowerInvariant(),'--output',$goaOutput)
    if ($Asset) { $goaArgs+=@('--asset',(Resolve-Path -LiteralPath $Asset).Path) }
    & $BlenderExe @goaArgs
    if ($LASTEXITCODE -ne 0) { throw 'Blender preparation/export failed; no Unity assets were replaced.' }
    Write-Output ('Report: '+(Join-Path $goaOutput 'report.json'))
} finally { $env:BLENDER_USER_CONFIG=$goaOldConfig }

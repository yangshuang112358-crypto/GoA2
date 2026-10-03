$ErrorActionPreference='Stop'
$goaRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$goaTemplate=Join-Path $goaRoot 'art/production/templates/Goa2_Studio.blend'
$goaWork=Join-Path $goaRoot 'art/production/assets/Workbench.blend'
if (-not (Test-Path -LiteralPath $goaWork)) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $goaWork)) | Out-Null
    [IO.File]::Copy($goaTemplate,$goaWork,$false)
}
& (Join-Path $PSScriptRoot 'blender.ps1') -Mode Open -Asset $goaWork

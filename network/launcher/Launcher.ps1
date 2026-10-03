param([switch]$Smoke)
& (Join-Path $PSScriptRoot 'AutoLauncher.ps1') -Smoke:$Smoke

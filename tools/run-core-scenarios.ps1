param([string[]]$Scenario = @(), [string]$DotnetExe)
$ErrorActionPreference = 'Stop'
$goaRoot = Split-Path -Parent $PSScriptRoot
if (-not $DotnetExe) {
    $goaLocalDotnet = Join-Path $env:LOCALAPPDATA 'Goa2V1Toolchain/dotnet/dotnet.exe'
    $DotnetExe = if (Test-Path -LiteralPath $goaLocalDotnet) { $goaLocalDotnet } else { 'dotnet' }
}
$goaFiles = if ($Scenario.Count) { @($Scenario | ForEach-Object { (Resolve-Path -LiteralPath $_).Path }) } else { @(Get-ChildItem -LiteralPath (Join-Path $goaRoot 'tests/scenarios') -Filter '*.json' -File | Sort-Object Name | Select-Object -ExpandProperty FullName) }
if (-not $goaFiles.Count) { throw 'No scenario files found.' }
$goaProject = Join-Path $PSScriptRoot 'Goa2.ScenarioCli/Goa2.ScenarioCli.csproj'
& $DotnetExe build $goaProject --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Core scenario CLI build failed.' }
$goaCli = Join-Path $PSScriptRoot 'Goa2.ScenarioCli/bin/Release/net10.0/Goa2.ScenarioCli.dll'
foreach ($goaFile in $goaFiles) {
    $goaOutput = Join-Path $goaRoot ('artifacts/core-scenarios/' + [IO.Path]::GetFileNameWithoutExtension($goaFile) + '/' + [Guid]::NewGuid().ToString('N'))
    & $DotnetExe $goaCli $goaRoot $goaFile $goaOutput
    if ($LASTEXITCODE -ne 0) { throw "Core scenario failed: $goaFile (exit $LASTEXITCODE)." }
}

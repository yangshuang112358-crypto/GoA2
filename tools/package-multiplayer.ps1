param([string]$PythonExe='python', [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Label='four-player')
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaDotnet=Join-Path $env:LOCALAPPDATA 'Goa2V1Toolchain/dotnet/dotnet.exe'
$goaStamp=$Label+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)
$goaPublished=Join-Path $goaRoot ('artifacts/network/publish-'+$goaStamp)
& $PythonExe -B (Join-Path $PSScriptRoot 'player_package.py') verify-build (Join-Path $goaRoot 'artifacts/player') --source-root $goaRoot
if ($LASTEXITCODE -ne 0) { throw 'Player is stale or invalid. Rebuild before packaging.' }
# Separate RID lock files keep ordinary SDK builds and portable publishing reproducible.
& $goaDotnet publish (Join-Path $goaRoot 'network/Goa2.Network/Goa2.Network.csproj') -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=packages.win-x64.lock.json -p:RestoreLockedMode=true -p:WarningsNotAsErrors=CS8602 -o $goaPublished
if ($LASTEXITCODE -ne 0) { throw 'Portable host publish failed.' }
$goaDestination=Join-Path $goaRoot ('artifacts/releases/Goa2V1-Multiplayer-'+$goaStamp)
& $PythonExe -B (Join-Path $PSScriptRoot 'multiplayer_package.py') create --root $goaRoot --host $goaPublished --destination $goaDestination
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer packaging failed.' }

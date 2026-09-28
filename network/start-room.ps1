param([string]$LanAddress='127.0.0.1',[ValidateRange(0,4)][int]$OpenLocalPlayers=4)
$ErrorActionPreference='Stop'
$goaNetRoot=Split-Path -Parent $PSScriptRoot
$goaDotnet=Join-Path $env:LOCALAPPDATA 'Goa2V1Toolchain/dotnet/dotnet.exe'
$goaExe=Join-Path $goaNetRoot 'artifacts/player/Goa2V1.exe'
if($OpenLocalPlayers -gt 0 -and -not (Test-Path -LiteralPath $goaExe)){throw 'Build the Windows player first.'}
& $goaDotnet build (Join-Path $PSScriptRoot 'Goa2.Network/Goa2.Network.csproj') -c Release -p:RestoreLockedMode=true
if($LASTEXITCODE -ne 0){throw 'Server build failed.'}
$goaRoom=Join-Path $goaNetRoot ('artifacts/network/room-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $goaRoom | Out-Null
$goaPrivate=Join-Path $goaRoom 'private'
$goaServer=Join-Path $PSScriptRoot 'Goa2.Network/bin/Release/net10.0/Goa2.Network.dll'
$goaArgs='"'+$goaServer+'" serve "'+$goaNetRoot+'" "'+$goaPrivate+'" "'+$LanAddress+'"'
$goaProcess=Start-Process -FilePath $goaDotnet -ArgumentList $goaArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $goaRoom 'server.log') -RedirectStandardError (Join-Path $goaRoom 'server-error.log')
$goaDeadline=[DateTime]::UtcNow.AddSeconds(20)
while(-not (Test-Path -LiteralPath (Join-Path $goaPrivate 'ready.json'))){
    if($goaProcess.HasExited -or [DateTime]::UtcNow -gt $goaDeadline){throw "Server did not start. Inspect $goaRoom"}
    Start-Sleep -Milliseconds 200
}
for($goaSeat=0;$goaSeat -lt $OpenLocalPlayers;$goaSeat++){
    & (Join-Path $PSScriptRoot 'join-room.ps1') -Player $goaExe -Ticket (Join-Path $goaPrivate "seat-$goaSeat.private.json")
}
Write-Output "Room: $goaRoom"
Write-Output 'Give each friend ONLY their own seat-N.private.json (N=0..3). Keep authority saves and other tickets private.'
Write-Output 'To stop and save: ./network/stop-room.ps1 -Room <the room path above>'

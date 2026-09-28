param([Parameter(Mandatory=$true)][string]$Ticket,[string]$Player)
$ErrorActionPreference='Stop'
if(-not $Player){$Player=Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/player/Goa2V1.exe'}
$goaTicket=(Resolve-Path -LiteralPath $Ticket).Path
$goaPlayer=(Resolve-Path -LiteralPath $Player).Path
$goaLog=Join-Path (Split-Path -Parent $goaTicket) ('player-'+[Guid]::NewGuid().ToString('N').Substring(0,8)+'.log')
$goaArgs='-goaNetworkTicket "'+$goaTicket+'" -screen-fullscreen 0 -screen-width 1600 -screen-height 1000 -logFile "'+$goaLog+'"'
# Interactive game window explicitly requested by invoking this launcher.
Start-Process -FilePath $goaPlayer -ArgumentList $goaArgs

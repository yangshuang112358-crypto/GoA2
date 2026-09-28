param([Parameter(Mandatory=$true)][string]$Room)
$ErrorActionPreference='Stop'
$goaRoomPath=(Resolve-Path -LiteralPath $Room).Path
$goaPrivate=Join-Path $goaRoomPath 'private'
if(-not (Test-Path -LiteralPath (Join-Path $goaPrivate 'ready.json'))){throw 'Not a room directory.'}
Set-Content -LiteralPath (Join-Path $goaPrivate 'stop.request') -Value 'stop' -Encoding ascii
$goaDeadline=[DateTime]::UtcNow.AddSeconds(20)
while(-not (Test-Path -LiteralPath (Join-Path $goaPrivate 'restore-check.json'))){
    if([DateTime]::UtcNow -gt $goaDeadline){throw 'No verified save yet. Inspect server.log; do not assume the save succeeded.'}
    Start-Sleep -Milliseconds 200
}
Write-Output 'Room stopped. Authority save exported and Restore verified in private/authority.private.save.json.'

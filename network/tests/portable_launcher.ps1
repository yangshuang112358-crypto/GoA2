param([Parameter(Mandatory=$true)][string]$Package, [string]$PythonExe='python')
$ErrorActionPreference='Stop'
$goaTestRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $Package 'launcher/RoomTools.ps1')
$goaOutput=Join-Path $goaTestRoot ('artifacts/network/portable-test-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
[IO.Directory]::CreateDirectory($goaOutput) | Out-Null
$goaChecks=New-Object 'Collections.Generic.List[string]'
function Check([string]$Name,[bool]$Condition) {
    if (-not $Condition) { throw ('Failed: '+$Name) }
    $goaChecks.Add($Name)
}
function Reject([string]$Name,[scriptblock]$Action) {
    $failed=$false
    try { & $Action | Out-Null } catch { $failed=$true }
    Check $Name $failed
}
$goaRoom=$null
try {
    Check 'loopback allowed for local test' (Test-GoaPrivateAddress '127.0.0.1')
    Check 'EasyTier private address allowed' (Test-GoaPrivateAddress '10.144.144.1')
    Check 'public address rejected' (-not (Test-GoaPrivateAddress '8.8.8.8'))
    Check 'IPv6 rejected until supported' (-not (Test-GoaPrivateAddress '::1'))
    Reject 'unassigned bind address rejected' { Start-GoaRoom $Package '10.254.254.254' $goaOutput }
    $goaRoom=Start-GoaRoom $Package '127.0.0.1' $goaOutput
    Check 'standalone executable started' (-not $goaRoom.Process.HasExited)
    Check 'live room reachable' (Test-GoaEndpoint $goaRoom.Address $goaRoom.Port)
    $goaInvites=Join-Path $goaOutput 'invitations'
    [IO.Directory]::CreateDirectory($goaInvites) | Out-Null
    for ($goaSeat=1; $goaSeat -le 3; $goaSeat++) {
        $goaFile=Join-Path $goaInvites ('friend-'+$goaSeat+'.json')
        Export-GoaInvitation $goaRoom $goaSeat $goaFile
        $goaTicket=Get-GoaTicket $goaFile $Package
        Check ('one-seat invitation '+$goaSeat) ($goaTicket.Credential -eq (Get-GoaTicket (Join-Path $goaRoom.Private "seat-$goaSeat.private.json") $Package).Credential)
    }
    Check 'no authority or host invite exported' (@(Get-ChildItem -LiteralPath $goaInvites).Count -eq 3)
    Reject 'invitation overwrite refused' { Export-GoaInvitation $goaRoom 1 (Join-Path $goaInvites 'friend-1.json') }
    $goaBad=Join-Path $goaOutput 'invalid.json'
    $goaTicket=Get-GoaTicket (Join-Path $goaInvites 'friend-1.json') $Package
    $goaTicket.Capabilities.EngineVersion=0
    $goaTicket | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $goaBad -Encoding UTF8
    Reject 'old engine invitation rejected' { Get-GoaTicket $goaBad $Package }
    [IO.File]::WriteAllText($goaBad, '{}')
    Reject 'incomplete invitation rejected' { Get-GoaTicket $goaBad $Package }
    & $PythonExe (Join-Path $PSScriptRoot 'portable_clients.py') $goaRoom.Path (Join-Path $goaOutput 'clients.json')
    if ($LASTEXITCODE -ne 0) { throw 'Four-client portable test failed.' }
    Check 'four clients protocol acceptance' $true
    Stop-GoaRoom $goaRoom
    Check 'graceful stop and Restore validation' $goaRoom.Process.HasExited
    Check 'stopped endpoint not reachable' (-not (Test-GoaEndpoint $goaRoom.Address $goaRoom.Port))
    Reject 'stopped room cannot export invitations' { Export-GoaInvitation $goaRoom 1 (Join-Path $goaOutput 'stale.json') }
    & $PythonExe (Join-Path $goaTestRoot 'tools/multiplayer_package.py') verify $Package
    if ($LASTEXITCODE -ne 0) { throw 'Running the launcher polluted or changed the package.' }
    Check 'package remains pristine' $true
    [pscustomobject]@{ passed=$true; runner='Windows PowerShell launcher helpers + standalone host'; cross_machine=$false; os_input=$false; checks=$goaChecks; package_sha256=(Get-FileHash -LiteralPath (Join-Path $Package 'multiplayer-build.json')).Hash } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $goaOutput 'report.json') -Encoding UTF8
    Write-Output ('PASS '+$goaChecks.Count+' launcher checks. Evidence: '+$goaOutput)
} finally {
    if ($null -ne $goaRoom -and -not $goaRoom.Process.HasExited) { Stop-GoaRoom $goaRoom }
}

param([Parameter(Mandatory=$true)][string]$Package)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../launcher/BootstrapTools.ps1')
$temp=Join-Path ([IO.Path]::GetTempPath()) ('goa-invite-test-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp) | Out-Null
$script:checks=0
function Check($Name, [scriptblock]$Body) { & $Body; $script:checks++; Write-Output "PASS $Name" }
function Reject([scriptblock]$Body) { $rejected=$false; try { & $Body | Out-Null } catch { $rejected=$true }; if (-not $rejected) { throw 'Expected rejection' } }
$info=Get-GoaJson (Join-Path $Package 'player/build-info.json') 4194304
$ticket=[pscustomobject]@{Type='Hello';RoomId=('a'*32);Credential=('b'*64);Host='127.0.0.1';Port=32123;Capabilities=[pscustomobject]@{WireVersion=1;EngineVersion=$info.EngineVersion;ProtocolVersion=$info.ProtocolVersion;ContentHash=$info.ContentHash;RulesVersion=$info.RulesVersion}}
$network=New-GoaNetwork @('tcp://127.0.0.1:11010')
$valid=New-GoaUnifiedInvitation $network $ticket 1
$path=Join-Path $temp 'invite.private.json'
function Save($v) { Write-GoaJson $path $v }
Check 'valid invite roundtrip' { Save $valid; $read=Read-GoaUnifiedInvitation $path $Package; if ($read.Seat -ne 1) { throw 'seat' } }
foreach ($seat in @(0,4,'1')) { Check "reject seat $seat" { $v=New-GoaUnifiedInvitation $network $ticket 1; $v.Seat=$seat; Save $v; Reject { Read-GoaUnifiedInvitation $path $Package } } }
Check 'reject wrong network profile' { $v=New-GoaUnifiedInvitation $network $ticket 1; $v.Network.Profile='shell'; Save $v; Reject { Read-GoaUnifiedInvitation $path $Package } }
Check 'reject shell/config injection' { $v=New-GoaUnifiedInvitation $network $ticket 1; $v.Network.Secret="bad`n[flags]"; Save $v; Reject { Read-GoaUnifiedInvitation $path $Package } }
Check 'reject non-loopback game endpoint' { $v=New-GoaUnifiedInvitation $network $ticket 1; $v.Ticket.Host='192.168.1.1'; Save $v; Reject { Read-GoaUnifiedInvitation $path $Package } }
Check 'reject incompatible version' { $v=New-GoaUnifiedInvitation $network $ticket 1; $v.Ticket.Capabilities.EngineVersion=-1; Save $v; Reject { Read-GoaUnifiedInvitation $path $Package } }
Check 'reject unsafe peer URI' { Reject { New-GoaNetwork @('file:///C:/Windows') } }
Check 'reject URI config injection' { Reject { New-GoaNetwork @('tcp://x:1"') } }
Check 'reject oversized invitation' { [IO.File]::WriteAllText($path,(' '*65537)); Reject { Read-GoaUnifiedInvitation $path $Package } }
Check 'reject legacy ticket at automatic entry' { Save $ticket; Reject { Read-GoaUnifiedInvitation $path $Package } }
Check 'network secret changes each room' { $a=New-GoaNetwork @('tcp://localhost:11010'); $b=New-GoaNetwork @('tcp://localhost:11010'); if ($a.Secret -eq $b.Secret -or $a.Name -eq $b.Name) { throw 'not random' } }
Check 'host config only exposes authority TCP' { $c=Get-GoaCoreConfig $network 0 32123 35000 35001 0; if ($c -notmatch 'no_tun = true' -or $c -notmatch 'tcp_whitelist = \["32123"\]' -or $c -notmatch 'udp_whitelist = \["0"\]' -or $c -match 'proxy_cidrs') { throw 'unsafe config' } }
Check 'guest forward binds loopback only' { $c=Get-GoaCoreConfig $network 2 32123 35000 35001 35002; if ($c -notmatch 'bind_addr = "127.0.0.1:35002"' -or $c -notmatch 'dst_addr = "10.233.42.1:32123"' -or $c -notmatch 'tcp_whitelist = \["0"\]') { throw 'wrong forward' } }
Check 'reject string relay mode' { $v=New-GoaUnifiedInvitation $network $ticket 1; $v.Network.RelayOnly='false'; Save $v; Reject { Read-GoaUnifiedInvitation $path $Package } }
Check 'relay compatibility mode disables hole punching' { $n=New-GoaNetwork @('tcp://localhost:11010') $true; $c=Get-GoaCoreConfig $n 0 32123 35000 35001 0; if ($c -notmatch 'disable_p2p = true' -or $c -notmatch 'disable_udp_hole_punching = true') { throw 'wrong relay mode' } }
Write-Output "Unified invitation checks: $script:checks"

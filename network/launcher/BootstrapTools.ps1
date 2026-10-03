# Automatic networking is an isolated userspace transport. No rules or seat authority here.
. (Join-Path $PSScriptRoot 'RoomTools.ps1')

function Write-GoaJson([string]$Path, $Value) {
    $json=$Value | ConvertTo-Json -Depth 24 -Compress
    $tmp=$Path+'.writing'
    [IO.File]::WriteAllText($tmp,$json,(New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($tmp,$Path,[NullString]::Value) }
    else { [IO.File]::Move($tmp,$Path) }
}
function New-GoaPrivateDirectory([string]$Path) {
    $dir=[IO.Directory]::CreateDirectory($Path)
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,[Security.Principal.SecurityIdentifier]'S-1-5-18')) {
        $rule=New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
        $acl.AddAccessRule($rule)
    }
    $dir.SetAccessControl($acl)
}
function Test-GoaPeers($Peers) {
    if (@($Peers).Count -lt 1 -or @($Peers).Count -gt 4) { throw '初始节点数量应为 1–4。' }
    foreach ($peer in $Peers) {
        # Only simple host:port transport endpoints, never URLs with paths/credentials/config substitutions.
        if ($peer -isnot [string] -or $peer.Length -gt 260 -or $peer -cnotmatch '^(tcp|udp)://[A-Za-z0-9][A-Za-z0-9.-]*:[0-9]{1,5}$') { throw '节点格式应为 tcp://主机:端口 或 udp://主机:端口。' }
        $uri=[Uri]$peer
        if ($uri.Port -lt 1 -or $uri.Port -gt 65535 -or $uri.Host.Length -gt 253) { throw '节点地址或端口无效。' }
    }
}
function New-GoaNetwork([string[]]$Peers,[bool]$RelayOnly=$false) {
    Test-GoaPeers $Peers
    $bytes=New-Object byte[] 32
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [pscustomobject]@{Profile='easytier-2.6.4-userspace-v1';Name=('goa2-'+[Guid]::NewGuid().ToString('N'));Secret=([BitConverter]::ToString($bytes).Replace('-','').ToLowerInvariant());Peers=@($Peers);RelayOnly=$RelayOnly}
}
function Test-GoaNetwork($Network) {
    if ($Network.Profile -cne 'easytier-2.6.4-userspace-v1' -or $Network.Name -cnotmatch '^goa2-[a-f0-9]{32}$' -or $Network.Secret -cnotmatch '^[a-f0-9]{64}$') { throw '邀请的网络信息无效。' }
    Test-GoaPeers $Network.Peers
    if ($Network.PSObject.Properties['RelayOnly'] -and $Network.RelayOnly -isnot [bool]) { throw '中继模式字段无效。' }
}
function New-GoaUnifiedInvitation($Network,$Ticket,[ValidateRange(1,3)][int]$Seat) {
    # Deep copy: updating a local endpoint must never mutate another seat's invitation.
    $v=[pscustomobject]@{Kind='Goa2UnifiedInvite';SchemaVersion=1;Seat=$Seat;Network=$Network;Ticket=$Ticket}
    return ($v | ConvertTo-Json -Depth 24 -Compress | ConvertFrom-Json)
}
function Read-GoaUnifiedInvitation([string]$Path,[string]$Package) {
    try {
        $v=Get-GoaJson $Path 65536
        if ($v.Kind -cne 'Goa2UnifiedInvite' -or $v.SchemaVersion -ne 1 -or $v.Seat -isnot [int] -or $v.Seat -lt 1 -or $v.Seat -gt 3) { throw 'invalid schema' }
        Test-GoaNetwork $v.Network
        if ($v.Ticket.Host -cne '127.0.0.1') { throw 'invalid endpoint' }
        Test-GoaTicketObject $v.Ticket $Package
        return $v
    } catch { throw '邀请无效或版本不一致。请使用新启动器生成的个人邀请和与房主相同的完整游戏包；旧邀请请走手动入口。' }
}
function Get-GoaFreePort {
    $listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
    try { $listener.Start(); return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}
function Get-GoaCoreConfig($Network,[ValidateRange(0,3)][int]$Seat,[int]$HostPort,[int]$ListenPort,[int]$RpcPort,[int]$ForwardPort) {
    Test-GoaNetwork $Network
    foreach ($port in @($HostPort,$ListenPort,$RpcPort)) { if ($port -lt 1 -or $port -gt 65535) { throw 'invalid port' } }
    if ($Seat -gt 0 -and ($ForwardPort -lt 1 -or $ForwardPort -gt 65535)) { throw 'invalid forward port' }
    $allowed=if ($Seat -eq 0) { $HostPort } else { 0 }
    $ip='10.233.42.'+($Seat+1)
    $config=@"
instance_name = "Goa2-seat-$Seat"
instance_id = "$([Guid]::NewGuid().ToString())"
hostname = "Goa2-seat-$Seat"
ipv4 = "$ip/24"
dhcp = false
listeners = ["tcp://0.0.0.0:$ListenPort", "udp://0.0.0.0:$ListenPort"]
tcp_whitelist = ["$allowed"]
udp_whitelist = ["0"]
[network_identity]
network_name = "$($Network.Name)"
network_secret = "$($Network.Secret)"
[flags]
no_tun = true
use_smoltcp = true
bind_device = false
disable_upnp = true
enable_ipv6 = false
accept_dns = false
enable_encryption = true
encryption_algorithm = "aes-gcm"
relay_network_whitelist = ""
latency_first = false
[console_logger]
level = "error"
"@
    foreach ($peer in $Network.Peers) { $config+="`n[[peer]]`nuri = `"$peer`"`n" }
    if ($Network.PSObject.Properties['RelayOnly'] -and $Network.RelayOnly) {
        $config=$config.Replace('no_tun = true',"no_tun = true`ndisable_p2p = true`ndisable_udp_hole_punching = true`ndisable_tcp_hole_punching = true")
    }
    if ($Seat -gt 0) { $config+="`n[[port_forward]]`nbind_addr = `"127.0.0.1:$ForwardPort`"`ndst_addr = `"10.233.42.1:$HostPort`"`nproto = `"tcp`"`n" }
    return $config
}
function Test-GoaCorePackage([string]$Package) {
    $lock=Get-GoaJson (Join-Path $Package 'easytier/component.json') 65536
    if ($lock.Version -cne '2.6.4') { throw '网络组件版本不正确，请重新解压完整包。' }
    foreach ($name in @('easytier-core.exe','easytier-cli.exe')) {
        $entry=@($lock.Files | Where-Object { $_.Name -ceq $name })
        $hasher=[Security.Cryptography.SHA256]::Create()
        $file=[IO.File]::OpenRead((Join-Path $Package ('easytier/'+$name)))
        try { $hash=[BitConverter]::ToString($hasher.ComputeHash($file)).Replace('-','') } finally { $file.Dispose(); $hasher.Dispose() }
        if ($entry.Count -ne 1 -or $hash -ine $entry[0].Sha256) { throw '网络组件缺失或损坏，请重新解压。' }
    }
}
function Invoke-GoaCoreStatus([string]$Package,[int]$RpcPort,[string]$Directory,[string]$Command='peer') {
    $out=Join-Path $Directory 'rpc.private.json'; $err=Join-Path $Directory 'rpc-error.log'
    $p=Start-Process -FilePath (Join-Path $Package 'easytier/easytier-cli.exe') -ArgumentList @('-p',"127.0.0.1:$RpcPort",'-o','json',$Command) -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
    $null=$p.Handle
    try {
        if (-not $p.WaitForExit(1800)) { $p.Kill(); $p.WaitForExit(); return $null }
        if ($p.ExitCode -ne 0) { return $null }
        return Get-GoaJson $out 1048576
    } catch { return $null } finally { $p.Dispose() }
}
function Test-GoaSeatHandshake($Ticket,[int]$ExpectedSeat) {
    $client=New-Object Net.Sockets.TcpClient
    try {
        $task=$client.ConnectAsync($Ticket.Host,[int]$Ticket.Port)
        if (-not $task.Wait(1500)) { return $false }
        $client.ReceiveTimeout=8000; $client.SendTimeout=4000
        $stream=$client.GetStream()
        $deadline=[DateTime]::UtcNow.AddSeconds(15)
        $hello=@{Type='Hello';RoomId=$Ticket.RoomId;Credential=$Ticket.Credential}
        foreach ($p in $Ticket.Capabilities.PSObject.Properties) { $hello[$p.Name]=$p.Value }
        $bytes=[Text.Encoding]::UTF8.GetBytes(($hello | ConvertTo-Json -Compress))
        $header=[BitConverter]::GetBytes([Net.IPAddress]::HostToNetworkOrder([int]$bytes.Length))
        $stream.Write($header,0,4); $stream.Write($bytes,0,$bytes.Length)
        $size=New-Object byte[] 4
        $offset=0
        while ($offset -lt 4) { if ([DateTime]::UtcNow -gt $deadline) { return $false }; $n=$stream.Read($size,$offset,4-$offset); if ($n -eq 0) { return $false }; $offset+=$n }
        $length=[Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($size,0))
        if ($length -lt 1 -or $length -gt 8388608) { return $false }
        $data=New-Object byte[] $length; $offset=0
        while ($offset -lt $length) { if ([DateTime]::UtcNow -gt $deadline) { return $false }; $n=$stream.Read($data,$offset,$length-$offset); if ($n -eq 0) { return $false }; $offset+=$n }
        $reply=[Text.Encoding]::UTF8.GetString($data) | ConvertFrom-Json
        return $reply.Type -ceq 'Welcome' -and $reply.RoomId -ceq $Ticket.RoomId -and $reply.Seat -eq $ExpectedSeat
    } catch { return $false } finally { $client.Dispose() }
}

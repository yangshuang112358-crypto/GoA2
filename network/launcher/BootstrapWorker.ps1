param([Parameter(Mandatory=$true)][string]$Run)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'BootstrapTools.ps1')
$Run=[IO.Path]::GetFullPath($Run)
$statePath=Join-Path $Run 'state.json'
$request=Get-GoaJson (Join-Path $Run 'request.private.json') 65536
$package=[IO.Path]::GetFullPath($request.Package)
$room=$null; $core=$null; $mutex=$null; $locked=$false; $failure=$null; $hostBridges=@()
$script:details=@{Mode=$request.Mode;Seat=0;RoomId='';Ticket='';Invitations='';RoomPath='';RpcPort=0;ListenPort=0;CorePid=0;HostPid=0}
function State([string]$Phase,[string]$Message) {
    $v=@{Phase=$Phase;Message=$Message;WorkerPid=$PID;UpdatedUtc=[DateTime]::UtcNow.ToString('o')}
    foreach ($key in $script:details.Keys) { $v[$key]=$script:details[$key] }
    Write-GoaJson $statePath $v
}
function Check-Cancel {
    if (Test-Path -LiteralPath (Join-Path $Run 'stop.request')) { throw [OperationCanceledException]::new('已取消。') }
    $parent=Get-Process -Id $request.ParentPid -ErrorAction SilentlyContinue
    if ($null -eq $parent -or $parent.StartTime.ToUniversalTime().Ticks.ToString() -ne $request.ParentStarted) { throw [OperationCanceledException]::new('启动器已关闭。') }
}
function Start-Core {
    $p=Start-Process -FilePath (Join-Path $package 'easytier/easytier-core.exe') -ArgumentList ('-c "'+$configPath+'" --rpc-portal 127.0.0.1:'+ $rpc) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Run 'network.log') -RedirectStandardError (Join-Path $Run 'network-error.log')
    $null=$p.Handle
    return $p
}
try {
    Add-Type -Path (Join-Path $PSScriptRoot 'OwnedProcessJob.cs')
    [GoaOwnedProcessJob]::AttachWorker()
    State 'Checking' '正在检查游戏和网络组件…'
    Check-Cancel
    Test-GoaCorePackage $package
    $seat=0
    if ($request.Mode -ceq 'Host') {
        $onlyRelay=$request.PSObject.Properties['RelayOnly'] -and $request.RelayOnly -eq $true
        $network=New-GoaNetwork $request.Peers $onlyRelay
        State 'StartingHost' '正在创建房间…'
        $room=Start-GoaRoom $package '127.0.0.1' $Run { Check-Cancel }
        $ticket=Get-GoaTicket (Join-Path $room.Private 'seat-0.private.json') $package
        $script:details.HostPid=$room.Process.Id; $script:details.RoomPath=$room.Path
        $invitations=Join-Path $Run 'invitations'
        New-GoaPrivateDirectory $invitations
        for ($i=1; $i -le 3; $i++) {
            $seatTicket=Get-GoaTicket (Join-Path $room.Private "seat-$i.private.json") $package
            # One transport network per guest: the authority is shared, mesh routing is not.
            $guestNetwork=if ($i -eq 1) { $network } else { New-GoaNetwork $request.Peers $onlyRelay }
            Write-GoaJson (Join-Path $invitations ("Goa2-席位"+($i+1)+".private.json")) (New-GoaUnifiedInvitation $guestNetwork $seatTicket $i)
            if ($i -gt 1) { $hostBridges+=@{Network=$guestNetwork;Process=$null;Rpc=0;Directory='';Ready=$false} }
        }
        $script:details.Invitations=$invitations
    } elseif ($request.Mode -ceq 'Join') {
        $invite=Read-GoaUnifiedInvitation (Join-Path $Run 'invitation.private.json') $package
        $network=$invite.Network; $seat=$invite.Seat; $ticket=$invite.Ticket
    } else { throw '启动方式无效。' }
    $script:details.Seat=$seat; $script:details.RoomId=$ticket.RoomId
    $mutex=New-Object Threading.Mutex($false,('Local\Goa2Auto-'+$ticket.RoomId+'-'+$seat))
    try { $locked=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked=$true }
    if (-not $locked) { throw '本机已经连接这个房间的同一席位，请使用原启动器。' }
    Check-Cancel
    $listen=Get-GoaFreePort; $rpc=Get-GoaFreePort; $forward=Get-GoaFreePort
    $script:details.RpcPort=$rpc; $script:details.ListenPort=$listen
    $config=Get-GoaCoreConfig $network $seat $ticket.Port $listen $rpc $forward
    $configPath=Join-Path $Run 'network.private.toml'
    [IO.File]::WriteAllText($configPath,$config,(New-Object Text.UTF8Encoding($false)))
    State 'StartingNetwork' '正在启动游戏专用网络（无需虚拟网卡）…'
    # The RPC portal is a process argument in 2.6.4, not a TOML property.
    $core=Start-Core
    $null=$core.Handle; $script:details.CorePid=$core.Id
    foreach ($bridge in $hostBridges) {
        Check-Cancel
        $bridge.Rpc=Get-GoaFreePort
        $bridge.Directory=Join-Path $Run ('bridge-'+$bridge.Rpc)
        New-GoaPrivateDirectory $bridge.Directory
        $bridgeConfig=Get-GoaCoreConfig $bridge.Network 0 $ticket.Port (Get-GoaFreePort) $bridge.Rpc 0
        $bridgePath=Join-Path $bridge.Directory 'network.private.toml'
        [IO.File]::WriteAllText($bridgePath,$bridgeConfig,(New-Object Text.UTF8Encoding($false)))
        $bridge.Process=Start-Process -FilePath (Join-Path $package 'easytier/easytier-core.exe') -ArgumentList ('-c "'+$bridgePath+'" --rpc-portal 127.0.0.1:'+$bridge.Rpc) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $bridge.Directory 'network.log') -RedirectStandardError (Join-Path $bridge.Directory 'network-error.log')
        $null=$bridge.Process.Handle
    }
    $script:details.CorePids=@($core.Id)+@($hostBridges | ForEach-Object { $_.Process.Id })
    $deadline=[DateTime]::UtcNow.AddSeconds(75)
    $fallbackAt=[DateTime]::UtcNow.AddSeconds(20); $relayFallback=$network.PSObject.Properties['RelayOnly'] -and $network.RelayOnly
    if ($seat -gt 0) { $ticket.Host='127.0.0.1'; $ticket.Port=$forward }
    $connected=$false
    State 'Connecting' $(if ($seat -eq 0) {'正在连接初始节点…'} else {'正在组网并寻找房主…'})
    while ([DateTime]::UtcNow -lt $deadline) {
        Check-Cancel
        if ($core.HasExited) { throw '网络组件退出，请重新解压完整包或查看本机诊断目录。' }
        if ($seat -gt 0 -and -not $relayFallback -and [DateTime]::UtcNow -gt $fallbackAt) {
            $relayFallback=$true
            State 'Connecting' '直连未完成，正在自动尝试中继连接…'
            $core.Kill(); $core.WaitForExit(5000) | Out-Null
            # Keep the exact same room, credential and virtual endpoint. Only the owned transport restarts.
            $config=$config.Replace('no_tun = true',"no_tun = true`ndisable_p2p = true`ndisable_udp_hole_punching = true`ndisable_tcp_hole_punching = true")
            [IO.File]::WriteAllText($configPath,$config,(New-Object Text.UTF8Encoding($false)))
            $core=Start-Core; $script:details.CorePid=$core.Id; $script:details.CorePids=@($core.Id)
        }
        $peers=Invoke-GoaCoreStatus $package $rpc $Run
        $remote=@($peers | Where-Object { $null -ne $_ -and $_.cost -ne 'Local' })
        if ($remote.Count -gt 0) {
            if ($seat -eq 0) {
                foreach ($bridge in $hostBridges) {
                    if ($bridge.Process.HasExited) { throw '好友通道启动失败，请保留诊断目录并重新开房。' }
                    if (-not $bridge.Ready) {
                        $bridgePeers=Invoke-GoaCoreStatus $package $bridge.Rpc $bridge.Directory
                        $bridge.Ready=@($bridgePeers | Where-Object { $null -ne $_ -and $_.cost -ne 'Local' }).Count -gt 0
                    }
                }
                if (@($hostBridges | Where-Object { -not $_.Ready }).Count -eq 0) { $connected=$true; break }
            }
            if ($seat -gt 0 -and @($peers | Where-Object { $null -ne $_ -and $_.ipv4 -eq '10.233.42.1' }).Count -gt 0) {
                State 'Authenticating' '已找到房主，正在验证个人席位…'
                if (Test-GoaSeatHandshake $ticket $seat) { $connected=$true; break }
            }
        }
        Start-Sleep -Milliseconds 700
    }
    if (-not $connected) { throw '连接超时：未找到初始节点、房主已关房或邀请身份不匹配。请确认房主保持开房和双方使用同版游戏；可取消后重试或改用手动入口。' }
    Check-Cancel
    $ticketPath=Join-Path $Run 'player.private.json'
    Write-GoaJson $ticketPath $ticket
    $script:details.Ticket=$ticketPath
    if ($seat -eq 0) { State 'Ready' '房间已准备好。请分别导出席位 2、3、4 的邀请，私发给三位好友。' }
    else { State 'Ready' ('连接成功，你是席位 '+($seat+1)+'。断线可在游戏设置中重连。') }
    while ($true) {
        Check-Cancel
        if ($core.HasExited) { throw '网络组件意外退出，请退出本次连接并重新加入。' }
        if (@($hostBridges | Where-Object { $_.Process.HasExited }).Count -gt 0) { throw '好友网络通道意外退出，请保留诊断目录；本次房间将保存后停止。' }
        if ($null -ne $room -and $room.Process.HasExited) { throw '房主服务已退出，本次房间已停止。请保留本机诊断与存档。' }
        Start-Sleep -Milliseconds 500
    }
} catch [OperationCanceledException] {
    # Normal cancellation/close; room still gets its verified save.
} catch {
    $failure=$_.Exception.Message
} finally {
    State 'Stopping' '正在停止本次网络并保存房间，请稍候…'
    if ($null -ne $room) {
        do {
            $retry=$false
            try { Stop-GoaRoom $room }
            catch {
                $failure='房间保存未通过验证，请保留本机诊断目录：'+$room.Path
                if (-not $room.Process.HasExited) {
                    $stopFile=Join-Path $Run 'stop.request'
                    if (Test-Path -LiteralPath $stopFile) { [IO.File]::Delete($stopFile) }
                    State 'StopFailed' '保存尚未完成，房主进程仍保留。请点击停止按钮重试，不要强制关机。'
                    while (-not (Test-Path -LiteralPath $stopFile)) {
                        $parent=Get-Process -Id $request.ParentPid -ErrorAction SilentlyContinue
                        if ($null -eq $parent -or $parent.StartTime.ToUniversalTime().Ticks.ToString() -ne $request.ParentStarted) { break }
                        Start-Sleep -Milliseconds 500
                    }
                    $retry=Test-Path -LiteralPath $stopFile
                    if ($retry) { $failure=$null; State 'Stopping' '正在重新等待房间保存…' }
                }
            }
        } while ($retry)
    }
    if ($null -ne $core -and -not $core.HasExited) { $core.Kill(); $core.WaitForExit(5000) | Out-Null }
    foreach ($bridge in $hostBridges) {
        if ($null -ne $bridge.Process -and -not $bridge.Process.HasExited) { $bridge.Process.Kill(); $bridge.Process.WaitForExit(5000) | Out-Null }
    }
    if ($locked) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
    if ($failure) { State 'Error' $failure } else { State 'Stopped' $(if ($null -ne $room) {'房间已停止并验证存档；重开需要重新发送邀请。'} else {'本次连接已结束。'}) }
}

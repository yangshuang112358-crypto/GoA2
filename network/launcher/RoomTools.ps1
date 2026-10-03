# Dot-source from the packaged launcher or verification script. No SDK is required.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-GoaPrivateAddress([string]$Address) {
    $parsed = $null
    if (-not [Net.IPAddress]::TryParse($Address, [ref]$parsed) -or $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) { return $false }
    $b = $parsed.GetAddressBytes()
    return [Net.IPAddress]::IsLoopback($parsed) -or $b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168)
}

function Get-GoaAddresses {
    foreach ($adapter in [Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
        if ($adapter.OperationalStatus -ne [Net.NetworkInformation.OperationalStatus]::Up) { continue }
        foreach ($item in $adapter.GetIPProperties().UnicastAddresses) {
            $address = $item.Address.ToString()
            if ((Test-GoaPrivateAddress $address) -and $address -ne '127.0.0.1') {
                [pscustomobject]@{ Address=$address; Label=($address+' — '+$adapter.Name) }
            }
        }
    }
    [pscustomobject]@{ Address='127.0.0.1'; Label='127.0.0.1 — 仅本机测试，好友不能加入' }
}

function Get-GoaDataRoot {
    $path = Join-Path $env:LOCALAPPDATA 'Goa2V1/Multiplayer'
    [IO.Directory]::CreateDirectory($path) | Out-Null
    return $path
}

function Get-GoaJson([string]$Path, [int]$MaxBytes=16384) {
    $file = Get-Item -LiteralPath $Path
    if ($file.PSIsContainer -or $file.Length -gt $MaxBytes) { throw '文件格式或大小不正确。' }
    try { return [IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json }
    catch { throw '无法读取 JSON 文件。请重新取得原始邀请文件或解压游戏包。' }
}

function Get-GoaTicket([string]$Path, [string]$Package) {
    try {
        $ticket = Get-GoaJson $Path
        Test-GoaTicketObject $ticket $Package
        return $ticket
    } catch { throw '邀请无效或游戏版本不一致。请使用与房主相同的游戏包和本次房间的个人邀请。' }
}

function Test-GoaTicketObject($ticket, [string]$Package) {
        if ($ticket.Type -ne 'Hello' -or $ticket.RoomId -notmatch '^[a-fA-F0-9]{32}$' -or
            $ticket.Credential -notmatch '^[a-fA-F0-9]{64}$' -or -not (Test-GoaPrivateAddress $ticket.Host) -or
            ($ticket.Port -isnot [int] -and $ticket.Port -isnot [long]) -or $ticket.Port -lt 1 -or $ticket.Port -gt 65535) { throw 'invalid' }
        $info = Get-GoaJson (Join-Path $Package 'player/build-info.json') 4194304
        $caps = $ticket.Capabilities
        if ($caps.WireVersion -ne 1 -or $caps.EngineVersion -ne $info.EngineVersion -or
            $caps.ProtocolVersion -cne $info.ProtocolVersion -or $caps.ContentHash -cne $info.ContentHash -or
            $caps.RulesVersion -cne $info.RulesVersion) { throw 'version' }
}

function Test-GoaEndpoint([string]$Address, [int]$Port) {
    $client = New-Object Net.Sockets.TcpClient
    try {
        $attempt = $client.BeginConnect($Address, $Port, $null, $null)
        try {
            if (-not $attempt.AsyncWaitHandle.WaitOne(2500)) { return $false }
            $client.EndConnect($attempt)
            return $true
        } finally { $attempt.AsyncWaitHandle.Close() }
    } catch { return $false }
    finally { $client.Dispose() }
}

function Start-GoaPlayer([string]$Package, [string]$Ticket) {
    $null = Get-GoaTicket $Ticket $Package
    $player = Join-Path $Package 'player/Goa2V1.exe'
    if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw '游戏程序缺失，请完整解压游戏包。' }
    $ticketPath = (Resolve-Path -LiteralPath $Ticket).Path
    $log = Join-Path (Get-GoaDataRoot) ('player-'+[Guid]::NewGuid().ToString('N')+'.log')
    $arguments = '-goaNetworkTicket "'+$ticketPath+'" -screen-fullscreen 0 -screen-width 1600 -screen-height 1000 -logFile "'+$log+'"'
    # This is the interactive game explicitly requested by the launcher button.
    Start-Process -FilePath $player -ArgumentList $arguments -PassThru
}

function Join-GoaRoom([string]$Package, [string]$Ticket) {
    $details = Get-GoaTicket $Ticket $Package
    if (-not (Test-GoaEndpoint $details.Host $details.Port)) {
        throw '连接不到房主。请确认房主尚未关房、双方 EasyTier 已连入同一网络，并允许房主服务通过该网络的防火墙。'
    }
    # Keep a private local copy; moving a downloaded invitation must not break reconnect.
    $folder = Join-Path (Get-GoaDataRoot) ('joined-'+[Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $copy = Join-Path $folder 'invitation.private.json'
    Copy-Item -LiteralPath $Ticket -Destination $copy
    return Start-GoaPlayer $Package $copy
}

function Start-GoaRoom([string]$Package, [string]$Address, [string]$DataRoot='', [scriptblock]$CheckCancelled={}) {
    if (-not (Test-GoaPrivateAddress $Address) -or $Address -notin @(Get-GoaAddresses | ForEach-Object { $_.Address })) {
        throw '请选择本机正在使用的私有 IPv4 地址；异地联机请选择 EasyTier 地址。'
    }
    $server = Join-Path $Package 'host/Goa2.Network.exe'
    if (-not (Test-Path -LiteralPath $server -PathType Leaf)) { throw '房主服务缺失，请完整解压游戏包。' }
    if (-not $DataRoot) { $DataRoot = Get-GoaDataRoot }
    $room = Join-Path $DataRoot ('room-'+[Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($room) | Out-Null
    $private = Join-Path $room 'private'
    $arguments = 'serve "'+$Package+'" "'+$private+'" "'+$Address+'"'
    $process = Start-Process -FilePath $server -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $room 'server.log') -RedirectStandardError (Join-Path $room 'server-error.log')
    # Windows PowerShell 5.1 can otherwise lose the exit code after the child exits.
    $null = $process.Handle
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        $readyPath = Join-Path $private 'ready.json'
        while (-not (Test-Path -LiteralPath $readyPath)) {
            & $CheckCancelled
            if ($process.HasExited -or [DateTime]::UtcNow -ge $deadline) { throw '房主服务未成功启动。请查看本机房间目录内 server-error.log。' }
            Start-Sleep -Milliseconds 100
        }
        # ready.json is written after all four tickets, but a filesystem observer can
        # still see it before the write closes. Ticket validation also checks Player compatibility.
        for ($seat=0; $seat -lt 4; $seat++) { $null = Get-GoaTicket (Join-Path $private "seat-$seat.private.json") $Package }
        $ready = $null
        do {
            try { $ready = Get-GoaJson $readyPath } catch { Start-Sleep -Milliseconds 50 }
        } while (-not $ready -and [DateTime]::UtcNow -lt $deadline)
        if (-not $ready -or $ready.ProcessId -ne $process.Id) { throw '房间启动信息不完整。' }
        return [pscustomobject]@{ Path=$room; Private=$private; Process=$process; Address=$Address; Port=$ready.Port; RoomId=$ready.RoomId }
    } catch {
        # Only the newly created child, before any player has been launched.
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
        throw
    }
}

function Export-GoaInvitation($Room, [ValidateRange(1,3)][int]$Seat, [string]$Destination) {
    if ($Room.Process.HasExited) { throw '房间已停止，请重新创建房间。' }
    if (Test-Path -LiteralPath $Destination) { throw '目标文件已存在，请选择新的文件名。' }
    Copy-Item -LiteralPath (Join-Path $Room.Private "seat-$Seat.private.json") -Destination $Destination
}

function Stop-GoaRoom($Room) {
    $save = Join-Path $Room.Private 'authority.private.save.json'
    $check = Join-Path $Room.Private 'restore-check.json'
    if (-not $Room.Process.HasExited) {
        [IO.File]::WriteAllText((Join-Path $Room.Private 'stop.request'), 'stop')
        if (-not $Room.Process.WaitForExit(20000)) { throw '房间尚未正常停止。请勿强制退出，稍后重试并检查服务日志。' }
    }
    if ($Room.Process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $save) -or
        -not (Test-Path -LiteralPath $check) -or -not (Get-GoaJson $check).passed) {
        throw '服务已退出，但没有成功验证的存档；不能视为正常保存。请保留房间目录。'
    }
}

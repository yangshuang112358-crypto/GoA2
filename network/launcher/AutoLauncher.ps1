param([switch]$Smoke,[string]$RenderPath='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'BootstrapTools.ps1')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$script:package=Split-Path -Parent $PSScriptRoot
$script:run=''; $script:worker=$null; $script:player=$null; $script:state=$null
$script:opened=$false; $script:closing=$false; $script:stopping=$false
$form=New-Object Windows.Forms.Form
$form.Text='Goa2V1 · 邀请好友联机'
$form.ClientSize=New-Object Drawing.Size(780,660)
$form.StartPosition='CenterScreen'; $form.FormBorderStyle='FixedDialog'; $form.MaximizeBox=$false
$form.Font=New-Object Drawing.Font('Microsoft YaHei UI',11)
function Label([string]$Text,[int]$X,[int]$Y,[int]$W,[int]$H) {
    $c=New-Object Windows.Forms.Label; $c.Text=$Text; $c.SetBounds($X,$Y,$W,$H); $form.Controls.Add($c); return $c
}
function Button([string]$Text,[int]$X,[int]$Y,[int]$W,[scriptblock]$Action) {
    $c=New-Object Windows.Forms.Button; $c.Text=$Text; $c.SetBounds($X,$Y,$W,42); $c.Add_Click($Action); $form.Controls.Add($c); return $c
}
function Report-Error($ErrorRecord) { $status.Text=$ErrorRecord.Exception.Message; [Windows.Forms.MessageBox]::Show($form,$status.Text,'操作未完成') | Out-Null }
function Is-Running($Process) { return $null -ne $Process -and -not $Process.HasExited }
function Start-Connection([string]$Mode,[string]$Invitation='') {
    if ((Is-Running $script:worker) -or (Is-Running $script:player)) { throw '请先结束当前连接并关闭游戏。' }
    $peers=@($nodes.Text -split '[\s,;]+' | Where-Object { $_ })
    if ($Mode -eq 'Host') { Test-GoaPeers $peers }
    else { $null=Read-GoaUnifiedInvitation $Invitation $script:package }
    $script:run=Join-Path (Get-GoaDataRoot) ('auto-'+[Guid]::NewGuid().ToString('N'))
    New-GoaPrivateDirectory $script:run
    if ($Mode -eq 'Join') { Copy-Item -LiteralPath $Invitation -Destination (Join-Path $script:run 'invitation.private.json') }
    $parent=Get-Process -Id $PID
    Write-GoaJson (Join-Path $script:run 'request.private.json') @{Mode=$Mode;Package=$script:package;Peers=$peers;ParentPid=$PID;ParentStarted=$parent.StartTime.ToUniversalTime().Ticks.ToString()}
    $script:state=$null; $script:opened=$false; $script:stopping=$false
    $args='-NoProfile -File "'+(Join-Path $PSScriptRoot 'BootstrapWorker.ps1')+'" -Run "'+$script:run+'"'
    $script:worker=Start-Process -FilePath 'powershell.exe' -ArgumentList $args -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $script:run 'worker.log') -RedirectStandardError (Join-Path $script:run 'worker-error.log')
    $status.Text='正在准备，请稍候。随时可以取消。'; Update-Controls
}
function Request-Stop {
    if (Is-Running $script:worker) {
        $script:stopping=$true
        [IO.File]::WriteAllText((Join-Path $script:run 'stop.request'),'stop')
        $status.Text='正在取消连接／保存房间…'; Update-Controls
    }
}
function Update-Controls {
    $alive=Is-Running $script:worker; $game=Is-Running $script:player
    $ready=$alive -and $null -ne $script:state -and $script:state.Phase -eq 'Ready' -and -not $script:stopping
    $create.Enabled=-not $alive -and -not $game; $join.Enabled=$create.Enabled; $nodes.Enabled=$create.Enabled
    $again.Enabled=$create.Enabled -and (Test-Path -LiteralPath (Join-Path (Get-GoaDataRoot) 'last-invite.private.json'))
    $enter.Enabled=$ready -and -not $game
    $export.Enabled=$ready -and $script:state.Mode -eq 'Host'; $seat.Enabled=$export.Enabled
    $stop.Enabled=$alive -and -not $script:stopping; $manual.Enabled=-not $alive -and -not $game
    $diagnostics.Enabled=[bool]$script:run
    $progress.Style=if ($alive -and -not $ready) {'Marquee'} else {'Blocks'}
    $progress.Value=if ($ready) {100} else {0}
    if ($alive -and $null -ne $script:state) {
        $phaseNames=@{Checking='检查组件';StartingHost='创建房间';StartingNetwork='准备网络';Connecting='寻找房间';Authenticating='验证席位';Ready='已连接';Stopping='正在保存';StopFailed='保存待重试';Stopped='已断开';Error='连接未完成'}
        $roomLabel.Text=$phaseNames[$script:state.Phase]
        if ($script:state.RoomId) { $roomLabel.Text='席位 '+($script:state.Seat+1)+' · '+$roomLabel.Text }
    }
    else { $roomLabel.Text='未连接' }
}
$title=Label '开房 → 私发邀请 → 好友导入并加入' 24 20 725 40
$title.Font=New-Object Drawing.Font('Microsoft YaHei UI',16,[Drawing.FontStyle]::Bold)
$null=Label "自动完成组网，不用另开 EasyTier，也不用填写 IP。`n四人使用同一版本的完整游戏包；房主请一直保持本窗口开启。" 24 66 727 60
$create=Button '创建房间并进入' 24 139 230 { try { Start-Connection 'Host' } catch { Report-Error $_ } }
$join=Button '导入邀请并加入' 270 139 230 {
    $d=New-Object Windows.Forms.OpenFileDialog
    try { $d.Filter='Goa2 私人邀请 (*.json)|*.json'; if ($d.ShowDialog($form) -eq 'OK') { Start-Connection 'Join' $d.FileName } }
    catch { Report-Error $_ } finally { $d.Dispose() }
}
$again=Button '重新加入上次房间' 516 139 240 { try { Start-Connection 'Join' (Join-Path (Get-GoaDataRoot) 'last-invite.private.json') } catch { Report-Error $_ } }
$roomLabel=Label '未连接' 24 196 730 28
$progress=New-Object Windows.Forms.ProgressBar; $progress.SetBounds(24,231,732,12); $form.Controls.Add($progress)
$status=Label '房主创建成功后，下面可分别导出三个邀请。好友各用一个，请勿共用同一席位。' 24 258 730 82
$status.ForeColor=[Drawing.Color]::FromArgb(25,65,90)
$enter=Button '重新打开游戏' 24 344 210 { try { $script:player=Start-GoaPlayer $script:package $script:state.Ticket; Update-Controls } catch { Report-Error $_ } }
$stop=Button '取消／停止本次连接' 250 344 250 {
    if ($null -ne $script:state -and $script:state.Mode -eq 'Host' -and $script:state.Phase -eq 'Ready') {
        if ([Windows.Forms.MessageBox]::Show($form,'停止将结束房间并断开所有玩家。当前不能从旧存档重开房间。是否停止？','停止房间','YesNo','Warning') -ne 'Yes') { return }
    }
    Request-Stop
}
$seat=New-Object Windows.Forms.ComboBox; $seat.SetBounds(24,412,176,36); $seat.DropDownStyle='DropDownList'
foreach ($name in @('好友一 · 席位 2','好友二 · 席位 3','好友三 · 席位 4')) { $null=$seat.Items.Add($name) }
$seat.SelectedIndex=0; $form.Controls.Add($seat)
$export=Button '导出这个好友的邀请' 216 406 284 {
    $d=New-Object Windows.Forms.SaveFileDialog
    try {
        $d.Filter='Goa2 私人邀请 (*.json)|*.json'; $d.FileName='Goa2-席位'+($seat.SelectedIndex+2)+'-'+$script:state.RoomId.Substring(0,6)+'.private.json'
        if ($d.ShowDialog($form) -eq 'OK') {
            if (Test-Path -LiteralPath $d.FileName) { throw '请使用新文件名，避免覆盖其他席位邀请。' }
            $source=Join-Path $script:state.Invitations ('Goa2-席位'+($seat.SelectedIndex+2)+'.private.json')
            Copy-Item -LiteralPath $source -Destination $d.FileName
            $status.Text='已导出 '+$seat.Text+'。把此文件私发给对应好友；新开房间须重新发邀请。'
        }
    } catch { Report-Error $_ } finally { $d.Dispose() }
}
$null=Label '高级：初始节点（一般不用改；只有创建房间时使用）' 24 463 730 28
$nodes=New-Object Windows.Forms.TextBox; $nodes.SetBounds(24,496,732,31)
$nodes.Text='tcp://38.147.105.185:11010'; $form.Controls.Add($nodes)
$manual=Button '手动／局域网入口' 24 546 230 {
    $null=Start-Process -FilePath 'powershell.exe' -ArgumentList ('-NoProfile -STA -File "'+(Join-Path $PSScriptRoot 'ManualLauncher.ps1')+'"') -WindowStyle Hidden
}
$diagnostics=Button '打开本机诊断目录' 270 546 230 { if ($script:run) { $null=Start-Process explorer.exe -ArgumentList ('"'+$script:run+'"') } }
$null=Label '连接优先尝试直连；免费公共节点可用性和跨网速度需实测。诊断目录含私密邀请，请勿整目录公开。' 24 607 730 45
$timer=New-Object Windows.Forms.Timer; $timer.Interval=400
$timer.Add_Tick({
    try {
        if ($script:run -and (Test-Path -LiteralPath (Join-Path $script:run 'state.json'))) {
            $next=Get-GoaJson (Join-Path $script:run 'state.json') 65536
            if ($null -eq $script:state -or $script:state.UpdatedUtc -ne $next.UpdatedUtc) { $status.Text=$next.Message }
            $script:state=$next
            if ($next.Phase -eq 'StopFailed') { $script:stopping=$false }
            if ($next.Phase -eq 'Ready' -and -not $script:opened -and -not $script:stopping -and (Is-Running $script:worker)) {
                $script:opened=$true
                if ($next.Mode -eq 'Join') { Copy-Item -LiteralPath (Join-Path $script:run 'invitation.private.json') -Destination (Join-Path (Get-GoaDataRoot) 'last-invite.private.json') -Force }
                $script:player=Start-GoaPlayer $script:package $next.Ticket
            }
        }
        if ($null -ne $script:worker -and $script:worker.HasExited -and ($null -eq $script:state -or $script:state.Phase -notin @('Error','Stopped'))) { $status.Text='启动进程意外退出，请打开本机诊断目录并保留记录；可重试或使用手动入口。' }
        Update-Controls
        if ($script:closing -and -not (Is-Running $script:worker)) { $form.Close() }
    } catch { $status.Text='启动检查未完成：'+$_.Exception.Message }
})
$form.Add_FormClosing({
    param($sender,$eventArgs)
    if (Is-Running $script:worker) {
        $eventArgs.Cancel=$true
        if (-not $script:closing -and [Windows.Forms.MessageBox]::Show($form,'关闭会结束本次连接；若你是房主，所有好友都会断线。要保存并退出吗？','退出联机','YesNo','Warning') -eq 'Yes') { $script:closing=$true; Request-Stop }
    }
})
Update-Controls; $timer.Start()
if ($Smoke) {
    $smokeTimer=New-Object Windows.Forms.Timer; $smokeTimer.Interval=1500
    $smokeTimer.Add_Tick({
        $smokeTimer.Stop()
        if ($RenderPath) {
            $bitmap=New-Object Drawing.Bitmap($form.Width,$form.Height)
            try { $form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height))); $bitmap.Save($RenderPath,[Drawing.Imaging.ImageFormat]::Png) }
            finally { $bitmap.Dispose() }
        }
        $form.Close()
    })
    $form.Add_Shown({$smokeTimer.Start()})
}
try { [Windows.Forms.Application]::Run($form) } finally { $timer.Dispose(); $form.Dispose() }

param([switch]$Smoke)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RoomTools.ps1')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$script:package = Split-Path -Parent $PSScriptRoot
$script:room = $null
$script:hostPlayer = $null
$script:joiningPlayer = $null
$script:busy = $false
$form = New-Object Windows.Forms.Form
$form.Text = 'Goa2V1 · 四人联机试玩'
$form.ClientSize = New-Object Drawing.Size(740, 590)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.Font = New-Object Drawing.Font('Microsoft YaHei UI', 11)

function Label([string]$Text, [int]$X, [int]$Y, [int]$W, [int]$H) {
    $control = New-Object Windows.Forms.Label
    $control.Text=$Text; $control.SetBounds($X,$Y,$W,$H)
    $form.Controls.Add($control)
    return $control
}
function Button([string]$Text, [int]$X, [int]$Y, [int]$W, [scriptblock]$Action) {
    $control = New-Object Windows.Forms.Button
    $control.Text=$Text; $control.SetBounds($X,$Y,$W,42)
    $control.Add_Click($Action); $form.Controls.Add($control)
    return $control
}
function Run-Action([scriptblock]$Action) {
    if ($script:busy) { return }
    $script:busy=$true; $form.UseWaitCursor=$true
    try { & $Action }
    catch { $status.Text=$_.Exception.Message; [Windows.Forms.MessageBox]::Show($form,$_.Exception.Message,'操作未完成') | Out-Null }
    finally { $script:busy=$false; $form.UseWaitCursor=$false; Update-Controls }
}
function Update-Controls {
    $alive = $null -ne $script:room -and -not $script:room.Process.HasExited
    $hostAlive = $null -ne $script:hostPlayer -and -not $script:hostPlayer.HasExited
    $joinAlive = $null -ne $script:joiningPlayer -and -not $script:joiningPlayer.HasExited
    $create.Enabled = -not $alive -and -not $joinAlive -and -not $hostAlive
    $address.Enabled = $create.Enabled; $refresh.Enabled=$create.Enabled
    $openGame.Enabled=$alive -and -not $hostAlive
    $export.Enabled=$alive; $seat.Enabled=$alive
    $stop.Enabled=$alive
    $join.Enabled=-not $alive -and -not $hostAlive -and -not $joinAlive
    if ($alive) { $roomLabel.Text='房间运行中：'+$script:room.Address+':'+$script:room.Port+'  · 房主为席位 1' }
    elseif ($null -ne $script:room) { $roomLabel.Text='房主服务已结束；原邀请不能再加入。' }
    else { $roomLabel.Text='尚未创建房间。' }
}
function Refresh-Addresses {
    $address.Items.Clear()
    foreach ($item in Get-GoaAddresses) { $address.Items.Add($item) | Out-Null }
    # Never silently select a LAN/Wi-Fi adapter in place of EasyTier.
    $address.SelectedIndex=-1
    for ($i=0; $i -lt $address.Items.Count; $i++) {
        if ($address.Items[$i].Label -match 'EasyTier') { $address.SelectedIndex=$i; break }
    }
}
$null=Label '四人免费联机 · 一人开房，三人加入' 22 18 690 34
$null=Label "请先在 EasyTier 中加入同一私人网络。此工具不会安装或代开 EasyTier。`n异地开房请选择它的虚拟地址；普通家庭局域网地址通常不能让异地好友加入。" 22 57 695 65
$null=Label '房主：选择本机连接地址' 22 129 520 28
$address=New-Object Windows.Forms.ComboBox
$address.SetBounds(22,162,540,36); $address.DropDownStyle='DropDownList'; $address.DisplayMember='Label'
$form.Controls.Add($address)
$refresh=Button '刷新地址' 578 158 140 { Run-Action { Refresh-Addresses } }
$create=Button '创建房间' 22 210 160 {
    Run-Action {
        if ($null -eq $address.SelectedItem) { throw '请选择一个本机地址。异地试玩应选择 EasyTier 网卡地址。' }
        $script:room=Start-GoaRoom $script:package $address.SelectedItem.Address
        $status.Text='房间已创建。导出三个不同的邀请，分别私发给三位好友。'
        $script:hostPlayer=Start-GoaPlayer $script:package (Join-Path $script:room.Private 'seat-0.private.json')
    }
}
$openGame=Button '进入房主游戏' 192 210 175 {
    Run-Action { $script:hostPlayer=Start-GoaPlayer $script:package (Join-Path $script:room.Private 'seat-0.private.json') }
}
$stop=Button '停止房间并保存' 378 210 200 {
    Run-Action {
        if ([Windows.Forms.MessageBox]::Show($form,'停止将断开所有玩家。目前不能重新打开旧房间继续游戏。是否停止？','停止房间','YesNo','Warning') -eq 'Yes') {
            Stop-GoaRoom $script:room
            $status.Text='已停止，存档导出且检查通过。当前不支持重新打开旧房。'
        }
    }
}
$roomLabel=Label '' 22 266 690 30
$seat=New-Object Windows.Forms.ComboBox
$seat.SetBounds(22,310,160,36); $seat.DropDownStyle='DropDownList'
foreach ($item in @('席位 2','席位 3','席位 4')) { $seat.Items.Add($item) | Out-Null }
$seat.SelectedIndex=0; $form.Controls.Add($seat)
$export=Button '导出该好友的邀请' 192 304 240 {
    Run-Action {
        $dialog=New-Object Windows.Forms.SaveFileDialog
        try {
            $dialog.Filter='Goa2 邀请 (*.json)|*.json'
            $dialog.FileName='Goa2-席位'+($seat.SelectedIndex+2)+'-'+$script:room.RoomId.Substring(0,6)+'.private.json'
            if ($dialog.ShowDialog($form) -eq 'OK') {
                Export-GoaInvitation $script:room ($seat.SelectedIndex+1) $dialog.FileName
                $status.Text='已导出 '+$seat.Text+' 的邀请。只私发给对应的一位好友，不要发到公开群。'
            }
        } finally { $dialog.Dispose() }
    }
}
$null=Label '好友：收到自己的邀请文件后，点击下面的按钮' 22 367 690 30
$join=Button '选择邀请文件并加入游戏' 22 405 310 {
    Run-Action {
        $dialog=New-Object Windows.Forms.OpenFileDialog
        try {
            $dialog.Filter='Goa2 邀请 (*.json)|*.json'
            if ($dialog.ShowDialog($form) -eq 'OK') {
                $script:joiningPlayer=Join-GoaRoom $script:package $dialog.FileName
                $status.Text='已启动游戏，席位与版本由游戏握手确认。断线后在游戏设置中重连。'
            }
        } finally { $dialog.Dispose() }
    }
}
$status=Label '使用同一版本游戏包。房主需保持电脑和服务运行；此版无自动房主接管。' 22 465 690 76
$null=Label '连接失败请先检查 EasyTier 是否互通。公共节点不保证速度，优先直连。' 22 544 690 30
$timer=New-Object Windows.Forms.Timer
$timer.Interval=1000
$timer.Add_Tick({ if (-not $script:busy) { Update-Controls } })
$form.Add_FormClosing({
    param($sender,$eventArgs)
    if ($script:busy) { $eventArgs.Cancel=$true; return }
    if ($null -ne $script:room -and -not $script:room.Process.HasExited) {
        if ([Windows.Forms.MessageBox]::Show($form,'房间仍在运行。关闭启动器会停止房间并断开所有玩家。要停止并退出吗？','退出房主启动器','YesNo','Warning') -ne 'Yes') { $eventArgs.Cancel=$true; return }
        try { Stop-GoaRoom $script:room }
        catch { $eventArgs.Cancel=$true; [Windows.Forms.MessageBox]::Show($form,$_.Exception.Message,'尚未正常停止') | Out-Null }
    }
})
Refresh-Addresses
Update-Controls
$timer.Start()
if ($Smoke) {
    # Visible startup-only automation; does not count as mouse/keyboard interaction.
    $smokeTimer=New-Object Windows.Forms.Timer
    $smokeTimer.Interval=1500
    $smokeTimer.Add_Tick({ $smokeTimer.Stop(); $form.Close() })
    $form.Add_Shown({ $smokeTimer.Start() })
}
try { [Windows.Forms.Application]::Run($form) }
finally { $timer.Dispose(); $form.Dispose() }

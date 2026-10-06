param([switch]$Smoke,[string]$RenderPath='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'HomeTools.ps1')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$package=Split-Path -Parent $PSScriptRoot
$script:homeChild=$null
$script:homeMode=''
$form=New-Object Windows.Forms.Form
$form.Text='Goa2V1 · 开始游戏'
$form.ClientSize=New-Object Drawing.Size(680,500)
$form.StartPosition='CenterScreen'; $form.FormBorderStyle='FixedDialog'; $form.MaximizeBox=$false
$form.AutoScaleMode='Dpi'
$form.Font=New-Object Drawing.Font('Microsoft YaHei UI',11)
$form.BackColor=[Drawing.Color]::FromArgb(20,29,37)
$form.ForeColor=[Drawing.Color]::FromArgb(235,231,220)
function Home-Label([string]$Text,[int]$X,[int]$Y,[int]$W,[int]$H,[int]$Size=11) {
    $label=New-Object Windows.Forms.Label; $label.Text=$Text; $label.SetBounds($X,$Y,$W,$H)
    $label.Font=New-Object Drawing.Font('Microsoft YaHei UI',$Size)
    $form.Controls.Add($label); return $label
}
function Home-Button([string]$Text,[int]$X,[int]$Y,[scriptblock]$Action) {
    $button=New-Object Windows.Forms.Button; $button.Text=$Text; $button.SetBounds($X,$Y,296,68)
    $button.FlatStyle='Flat'; $button.FlatAppearance.BorderSize=1
    $button.FlatAppearance.BorderColor=[Drawing.Color]::FromArgb(169,145,95)
    $button.BackColor=[Drawing.Color]::FromArgb(35,48,60)
    $button.FlatAppearance.MouseOverBackColor=[Drawing.Color]::FromArgb(57,73,84)
    $button.Font=New-Object Drawing.Font('Microsoft YaHei UI',14)
    $button.Add_Click($Action); $form.Controls.Add($button); return $button
}
function Home-Launch([string]$Mode) {
    try {
        if ($Mode -ne 'Help' -and $null -ne $script:homeChild -and -not $script:homeChild.HasExited) { return }
        $started=Start-GoaHomeMode $package $Mode
        if ($Mode -ne 'Help') { $script:homeChild=$started; $script:homeMode=$Mode; Home-Update }
    } catch { [Windows.Forms.MessageBox]::Show($form,$_.Exception.Message,'启动未完成') | Out-Null }
}
function Home-Update {
    $running=$null -ne $script:homeChild -and -not $script:homeChild.HasExited
    $local.Enabled=-not $running; $tutorial.Enabled=-not $running; $network.Enabled=-not $running
    $status.Text=if ($running -and $script:homeMode -eq 'Network') {'联机窗口已打开。开房与加入请在该窗口完成，并保持它运行。'} elseif ($running) {'游戏已打开。关闭游戏后，可在这里选择其他模式。'} else {'第一次玩？建议先完成新手教程，再邀请好友。'}
}
$heading=Home-Label 'GUARDS OF ATLANTIS II' 32 24 616 50 24
$heading.ForeColor=[Drawing.Color]::FromArgb(210,183,127)
$null=Home-Label 'Goa2V1  ·  本地练习 / 四人邀请联机' 34 79 612 32 12
$tutorial=Home-Button '新手教程' 32 137 { Home-Launch 'Tutorial' }
$local=Home-Button '本地对局' 352 137 { Home-Launch 'Local' }
$null=Home-Label '单人分章练习，可退出后继续' 34 214 292 30
$null=Home-Label '同一电脑操作四个席位' 354 214 292 30
$network=Home-Button '好友联机' 32 265 { Home-Launch 'Network' }
$help=Home-Button '使用说明' 352 265 { Home-Launch 'Help' }
$null=Home-Label '一人开房，三人导入个人邀请' 34 342 292 30
$null=Home-Label '首次启动、操作、更新与排错' 354 342 292 30
$status=Home-Label '' 34 391 612 44
$status.ForeColor=[Drawing.Color]::FromArgb(211,183,130)
$versionText='Windows x64 完整试玩包 · 音量与热键在游戏设置中'
if (Test-Path -LiteralPath (Join-Path $package 'version.txt')) { $versionText=([IO.File]::ReadAllText((Join-Path $package 'version.txt')) -split "`n")[0].Trim() }
$version=Home-Label $versionText 34 457 612 30 10
$version.ForeColor=[Drawing.Color]::FromArgb(159,174,184)
$timer=New-Object Windows.Forms.Timer; $timer.Interval=500; $timer.Add_Tick({ Home-Update })
Home-Update; $timer.Start()
if ($Smoke) {
    $smokeTimer=New-Object Windows.Forms.Timer; $smokeTimer.Interval=1200
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
try { [Windows.Forms.Application]::Run($form) }
finally { $timer.Dispose(); if ($Smoke) { $smokeTimer.Dispose() }; $form.Dispose() }

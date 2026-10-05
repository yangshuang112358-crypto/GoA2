$ErrorActionPreference='Stop'
$uiRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$uiOutput=Join-Path $uiRoot 'artifacts/ui3d/player'
$uiChecks=[System.Collections.Generic.List[string]]::new()
$uiReport=[ordered]@{method='OS key/mouse injection into verified foreground standalone Player; not human physical input and not UI Toolkit synthetic events';passed=$false;error='';checks=$uiChecks}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GoaUI3DInput {
 [StructLayout(LayoutKind.Sequential)] public struct POINT {public int X,Y;}
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref POINT p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code,uint map);
 [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,int d,UIntPtr e);
}
'@
function State {Get-Content -LiteralPath (Join-Path $uiOutput 'render.board3d.json') -Raw | ConvertFrom-Json}
function Check([bool]$condition,[string]$text) {if(-not $condition){throw $text};$uiChecks.Add($text)}
function Guard {if([GoaUI3DInput]::GetForegroundWindow() -ne $uiHandle) {throw 'Player is not foreground; no further input sent.'}}
function Key([byte]$code) {Guard;$scan=[byte][GoaUI3DInput]::MapVirtualKey($code,0);$flags=[uint32]0;if($code -eq 0x24){$flags=1};[GoaUI3DInput]::keybd_event($code,$scan,$flags,[UIntPtr]::Zero);Start-Sleep -Milliseconds 120;[GoaUI3DInput]::keybd_event($code,$scan,($flags -bor 2),[UIntPtr]::Zero);Start-Sleep -Milliseconds 500}
function Pointer([int]$x,[int]$y) {Guard;[GoaUI3DInput]::SetCursorPos($uiOrigin.X+$x,$uiOrigin.Y+$y) | Out-Null}
function ClickButton([string]$name,[string]$caption) {
 $layout=Get-Content -LiteralPath (Join-Path $uiOutput 'render.ui.json') -Raw | ConvertFrom-Json
 $button=@($layout.Buttons | Where-Object {if($name){$_.Name -eq $name}else{$_.Text -eq $caption}})[0]
 if(-not $button -or -not $button.Visible -or -not $button.Enabled){throw "Button not usable: $name $caption"}
 Pointer ([int]($button.Bounds.x+$button.Bounds.width/2)) ([int]($button.Bounds.y+$button.Bounds.height/2))
 [GoaUI3DInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 80;[GoaUI3DInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 450
}
try {
 $uiProcess=Get-Process -Id ([int](Get-Content -LiteralPath (Join-Path $uiOutput 'player.pid')))
 if($uiProcess.Path -ne (Join-Path $uiRoot 'artifacts/player/Goa2V1.exe')) {throw 'Refusing input to another worktree or executable.'}
 $uiDeadline=[DateTime]::UtcNow.AddSeconds(20)
 do {
  $uiFresh=Get-Item -LiteralPath (Join-Path $uiOutput 'render.board3d.json') -ErrorAction SilentlyContinue
  if($uiFresh -and $uiFresh.LastWriteTimeUtc -gt $uiProcess.StartTime.ToUniversalTime()) {break}
  Start-Sleep -Milliseconds 200
 } while([DateTime]::UtcNow -lt $uiDeadline)
 if(-not $uiFresh -or $uiFresh.LastWriteTimeUtc -le $uiProcess.StartTime.ToUniversalTime()) {throw 'No fresh telemetry from this Player; old results cannot establish readiness.'}
 Start-Sleep -Milliseconds 500
 $uiProcess.Refresh()
 $uiHandle=$uiProcess.MainWindowHandle
 [GoaUI3DInput]::SetProcessDPIAware() | Out-Null
 [GoaUI3DInput]::SetForegroundWindow($uiHandle) | Out-Null
 Start-Sleep -Milliseconds 250;Guard
 $uiOrigin=[GoaUI3DInput+POINT]::new();[GoaUI3DInput]::ClientToScreen($uiHandle,[ref]$uiOrigin) | Out-Null
 $initial=State
 Check ($initial.Follow) 'Starts in main flow'
 Key 0x24;Check (-not (State).Follow) 'Home enters free viewing'
 Key 0x20;Check ((State).Follow) 'Space returns to current action'
 Key 0x20;Check ((State).Follow) 'Repeated Space does not toggle follow off'
 Check ((State).Revision -eq $initial.Revision) 'Focus shortcut submits no game command'
 Key 0x24;ClickButton 'main-flow-status' '';Check ((State).Follow) 'Clicking unified guide restores current action'
 $layout=Get-Content -LiteralPath (Join-Path $uiOutput 'render.ui.json') -Raw | ConvertFrom-Json
 $guide=@($layout.Buttons | Where-Object {$_.Name -eq 'main-flow-status'})[0]
 Check ($guide.Visible -and $guide.Bounds.width -le 282) 'Compact guide is visible and bounded'
 Check (@($layout.Buttons | Where-Object {$_.Name -eq 'follow-toggle' -and $_.Visible}).Count -eq 0) 'Old follow button is absent'
 $s=State;$x=[int]($s.Bounds.x+$s.Bounds.width*.58);$y=[int]($s.Bounds.y+$s.Bounds.height*.23)
 Pointer $x $y;[GoaUI3DInput]::mouse_event(0x20,0,0,0,[UIntPtr]::Zero)
 try {for($i=1;$i -le 6;$i++){Pointer ($x+$i*10) ($y+$i*5);Start-Sleep -Milliseconds 60}} finally {[GoaUI3DInput]::mouse_event(0x40,0,0,0,[UIntPtr]::Zero)}
 Start-Sleep -Milliseconds 450
 Check (-not (State).Follow) 'Middle drag leaves main flow'
 $manual=State;Start-Sleep -Milliseconds 700;Check ([Math]::Abs((State).Focus.x-$manual.Focus.x) -lt .001) 'Free camera stays where released'
 ClickButton 'main-flow-status' '';Check ((State).Follow) 'Return guide works after actual drag'
 Check ((State).Revision -eq $initial.Revision) 'All viewing operations preserve rule revision'
 $uiReport.passed=$true
} catch {$uiReport.error=$_.Exception.Message;Write-Output $_} finally {
 $uiReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $uiOutput 'main-flow-os-input-report.json') -Encoding utf8
}
if(-not $uiReport.passed) {throw 'OS input checks incomplete; see artifacts/ui3d/player/main-flow-os-input-report.json'}
Write-Output 'Main flow OS input checks passed'

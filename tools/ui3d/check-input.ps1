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
 [DllImport("user32.dll")] public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,int d,UIntPtr e);
}
'@
function State {Get-Content -LiteralPath (Join-Path $uiOutput 'render.board3d.json') -Raw | ConvertFrom-Json}
function Check([bool]$condition,[string]$text) {if(-not $condition){throw $text};$uiChecks.Add($text)}
function Guard {if([GoaUI3DInput]::GetForegroundWindow() -ne $uiHandle) {throw 'Player is not foreground; no further input sent.'}}
function Key([byte]$code) {Guard;[GoaUI3DInput]::keybd_event($code,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 90;[GoaUI3DInput]::keybd_event($code,0,2,[UIntPtr]::Zero);Start-Sleep -Milliseconds 350}
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
 Key 0x51;Check ((State).Step -eq (($initial.Step+11)%12)) 'Q rotates one step left'
 Key 0x45;Check ((State).Step -eq $initial.Step) 'E reverses Q'
 for($i=1;$i -le 12;$i++) {Key 0x45;Check ((State).Step -eq (($initial.Step+$i)%12)) "E step $i"}
 Check ((State).Revision -eq $initial.Revision) 'Rotation does not submit a game command'
 $s=State;$x=[int]($s.Bounds.x+$s.Bounds.width/2);$y=[int]($s.Bounds.y+$s.Bounds.height/2)
 Pointer $x $y;[GoaUI3DInput]::mouse_event(0x800,0,0,120,[UIntPtr]::Zero);Start-Sleep -Milliseconds 450
 Check ([Math]::Abs((State).Zoom-$s.Zoom) -gt .01) 'Wheel zoom reaches actual board'
 $before=State;Pointer $x $y;[GoaUI3DInput]::mouse_event(0x20,0,0,0,[UIntPtr]::Zero)
 try {for($i=1;$i -le 6;$i++){Pointer ($x+$i*10) ($y+$i*5);Start-Sleep -Milliseconds 60}} finally {[GoaUI3DInput]::mouse_event(0x40,0,0,0,[UIntPtr]::Zero)}
 Start-Sleep -Milliseconds 400
 Check ([Math]::Abs((State).Focus.x-$before.Focus.x) -gt .01) 'Middle drag pans camera'
 ClickButton 'toggle-3d' '';Check (-not (State).Is3D) '2D fallback click'
 ClickButton 'toggle-3d' '';Check ((State).Is3D) '2.5D return click'
 Check ((State).Revision -eq $initial.Revision) 'Camera and fallback preserve game revision'
 $uiReport.passed=$true
} catch {$uiReport.error=$_.Exception.Message;Write-Output $_} finally {
 $uiReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $uiOutput 'os-input-report.json') -Encoding utf8
}
if(-not $uiReport.passed) {throw 'OS input checks incomplete; see artifacts/ui3d/player/os-input-report.json'}
Write-Output 'OS input checks passed'

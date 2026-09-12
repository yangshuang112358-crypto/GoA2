param(
    [ValidateSet("Capture","Click","Scroll","Key","Drag","Hover")][string]$Action = "Capture",
    [int]$X = 0, [int]$Y = 0,
    [string]$Caption,
    [string]$Element,
    [switch]$AutoScroll,
    [ValidateSet("1","2","3","4","Home","Escape","Tab","Enter","Space","F1")][string]$Key = "1",
    [int]$ToX = 0, [int]$ToY = 0,
    [ValidateRange(-6000,6000)][int]$WheelDelta = -960,
    [ValidatePattern("^[a-zA-Z0-9_-]+$")][string]$Name = "player"
)
$ErrorActionPreference = "Stop"
$goaRoot = Split-Path -Parent $PSScriptRoot
$goaPidFile = Join-Path $goaRoot "artifacts\unity\player.pid"
$goaPlayer = Get-Process -Id ([int](Get-Content -LiteralPath $goaPidFile))
if ($goaPlayer.ProcessName -ne "Goa2V1") { throw "QA only targets the Goa2V1 player." }
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Goa2PlayerWindow {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref POINT p);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,int d,UIntPtr extra);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
}
'@
[Goa2PlayerWindow]::SetProcessDPIAware() | Out-Null
$goaHandle = $goaPlayer.MainWindowHandle
[Goa2PlayerWindow]::SetForegroundWindow($goaHandle) | Out-Null
$goaRect = [Goa2PlayerWindow+RECT]::new()
$goaPoint = [Goa2PlayerWindow+POINT]::new()
[Goa2PlayerWindow]::GetClientRect($goaHandle,[ref]$goaRect) | Out-Null
[Goa2PlayerWindow]::ClientToScreen($goaHandle,[ref]$goaPoint) | Out-Null
if ($Action -eq "Key") {
    if ([Goa2PlayerWindow]::GetForegroundWindow() -ne $goaHandle) { throw "Player is not foreground; no input sent." }
    $goaKeyCodes = @{ '1'=0x31; '2'=0x32; '3'=0x33; '4'=0x34; Home=0x24; Escape=0x1B; Tab=0x09; Enter=0x0D; Space=0x20; F1=0x70 }
    # Home is an extended navigation key; without its scan code/flag Unity may see Keypad7.
    $goaExtended = if ($Key -eq 'Home') { 1 } else { 0 }
    $goaScan = if ($Key -eq 'Home') { 0x47 } else { 0 }
    [Goa2PlayerWindow]::keybd_event($goaKeyCodes[$Key],$goaScan,$goaExtended,[UIntPtr]::Zero)
    [System.Threading.Thread]::Sleep(100)
    [Goa2PlayerWindow]::keybd_event($goaKeyCodes[$Key],$goaScan,($goaExtended -bor 2),[UIntPtr]::Zero)
    [System.Threading.Thread]::Sleep(350)
    Write-Output "Key $Key sent to player"
} elseif ($Action -ne "Capture") {
    if ($Caption -or $Element) {
        $goaDeadline = [DateTime]::UtcNow.AddSeconds($(if ($AutoScroll) { 15 } else { 3 }))
        do {
            $goaLayout = Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json') -Raw | ConvertFrom-Json
            $goaCandidates=@($goaLayout.Buttons)
            if($Element) { $goaCandidates+=@($goaLayout.Fields)+@($goaLayout.TextFields) }
            $goaMatches = @($goaCandidates | Where-Object { $_.Enabled -and $(if ($Element) { $_.Name -eq $Element } else { $_.Text -match $Caption }) })
            if ($goaMatches.Count -eq 1) {
                if (-not $AutoScroll -or $goaMatches[0].Visible) { break }
                if ([Goa2PlayerWindow]::GetForegroundWindow() -ne $goaHandle) { throw 'Player is not foreground; no scroll input sent.' }
                $goaTargetX=$goaMatches[0].Bounds.x+$goaMatches[0].Bounds.width/2
                $goaTargetY=$goaMatches[0].Bounds.y+$goaMatches[0].Bounds.height/2
                $goaViewport=@($goaMatches[0].Viewports | Where-Object { ($_.x+$_.width/2) -ge 0 -and ($_.x+$_.width/2) -lt $goaRect.Right -and ($_.y+$_.height/2) -ge 0 -and ($_.y+$_.height/2) -lt $goaRect.Bottom -and ($goaTargetX -lt $_.x -or $goaTargetX -ge ($_.x+$_.width) -or $goaTargetY -lt $_.y -or $goaTargetY -ge ($_.y+$_.height)) } | Select-Object -Last 1)
                if($goaViewport.Count) {
                    $goaClip=$goaViewport[0]
                    $goaScrollX=[int][Math]::Clamp($goaClip.x+$goaClip.width-2,1,$goaRect.Right-2)
                    $goaScrollY=[int][Math]::Clamp($goaClip.y+$goaClip.height/2,1,$goaRect.Bottom-2)
                    $goaHorizontal=$goaTargetX -lt $goaClip.x -or $goaTargetX -ge ($goaClip.x+$goaClip.width)
                    $goaScrollDelta=if($goaHorizontal) {if($goaTargetX -lt $goaClip.x){-360}else{360}} else {if($goaTargetY -lt $goaClip.y){360}else{-360}}
                } else {
                    $goaScrollX=[int][Math]::Clamp($goaTargetX,1,$goaRect.Right-2);$goaScrollY=[int](($goaRect.Bottom+150)/2)
                    $goaHorizontal=$false;$goaScrollDelta=if($goaTargetY -lt 150){360}else{-360}
                }
                $goaBar=$null
                if($goaHorizontal -and $goaViewport.Count) {
                    $goaBar=$goaLayout.Scrolls | Where-Object { [Math]::Abs($_.Viewport.x-$goaClip.x) -lt 1 -and [Math]::Abs($_.Viewport.y-$goaClip.y) -lt 1 -and $_.Maximum -gt 0 -and $_.Thumb.width -gt 0 } | Select-Object -First 1
                }
                if($goaBar) {
                    $goaDragX=$goaBar.Thumb.x+$goaBar.Thumb.width/2;$goaDragY=$goaBar.Thumb.y+$goaBar.Thumb.height/2
                    if($goaDragX -lt 0 -or $goaDragX -ge $goaRect.Right -or $goaDragY -lt 0 -or $goaDragY -ge $goaRect.Bottom) { throw 'Horizontal scrollbar is outside the window; reveal its panel first.' }
                    $goaDesired=[Math]::Clamp($goaBar.Value+$goaTargetX-($goaClip.x+$goaClip.width/2),0,$goaBar.Maximum)
                    $goaEndX=$goaBar.Track.x+$goaBar.Thumb.width/2+($goaBar.Track.width-$goaBar.Thumb.width)*$goaDesired/$goaBar.Maximum
                    $goaEndX=[Math]::Clamp($goaEndX,1,$goaRect.Right-2)
                    [Goa2PlayerWindow]::SetCursorPos($goaPoint.X+[int]$goaDragX,$goaPoint.Y+[int]$goaDragY) | Out-Null
                    [Goa2PlayerWindow]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
                    try {
                        for($goaDragStep=1;$goaDragStep -le 6;$goaDragStep++) {
                            [Goa2PlayerWindow]::SetCursorPos($goaPoint.X+[int]($goaDragX+($goaEndX-$goaDragX)*$goaDragStep/6),$goaPoint.Y+[int]$goaDragY) | Out-Null
                            [System.Threading.Thread]::Sleep(35)
                        }
                    } finally { [Goa2PlayerWindow]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero) }
                } else {
                    [Goa2PlayerWindow]::SetCursorPos($goaPoint.X+$goaScrollX,$goaPoint.Y+$goaScrollY) | Out-Null
                    [Goa2PlayerWindow]::mouse_event($(if($goaHorizontal){0x1000}else{0x0800}),0,0,$goaScrollDelta,[UIntPtr]::Zero)
                }
                [System.Threading.Thread]::Sleep(250)
            }
            [System.Threading.Thread]::Sleep(100)
        } while ([DateTime]::UtcNow -lt $goaDeadline)
        if ($goaMatches.Count -ne 1) { throw "Selector must match exactly one enabled button; matched $($goaMatches.Count)." }
        if ($AutoScroll -and -not $goaMatches[0].Visible) { throw 'Button did not become visible after scrolling.' }
        $X = [int]($goaMatches[0].Bounds.x + $goaMatches[0].Bounds.width / 2)
        $Y = [int]($goaMatches[0].Bounds.y + $goaMatches[0].Bounds.height / 2)
        if($Element -and @($goaLayout.Fields | Where-Object Name -eq $Element).Count) { $X=[int]($goaMatches[0].Bounds.x+$goaMatches[0].Bounds.width-65) }
    }
    if ([Goa2PlayerWindow]::GetForegroundWindow() -ne $goaHandle) { throw "Player is not foreground; no input sent." }
    if ($X -lt 0 -or $Y -lt 0 -or $X -ge $goaRect.Right -or $Y -ge $goaRect.Bottom) { throw "Point is outside the player." }
    [Goa2PlayerWindow]::SetCursorPos($goaPoint.X + $X, $goaPoint.Y + $Y) | Out-Null
    if ($Action -eq "Hover") {
        [System.Threading.Thread]::Sleep(350)
    } elseif ($Action -eq "Scroll") {
        [Goa2PlayerWindow]::mouse_event(0x0800,0,0,$WheelDelta,[UIntPtr]::Zero)
    } elseif ($Action -eq "Drag") {
        if ($ToX -lt 0 -or $ToY -lt 0 -or $ToX -ge $goaRect.Right -or $ToY -ge $goaRect.Bottom) { throw "Drag target is outside the player." }
        [Goa2PlayerWindow]::mouse_event(0x0020,0,0,0,[UIntPtr]::Zero)
        try {
            for ($goaStep = 1; $goaStep -le 10; $goaStep++) {
                [Goa2PlayerWindow]::SetCursorPos($goaPoint.X + $X + [int](($ToX-$X)*$goaStep/10), $goaPoint.Y + $Y + [int](($ToY-$Y)*$goaStep/10)) | Out-Null
                [System.Threading.Thread]::Sleep(30)
            }
        } finally { [Goa2PlayerWindow]::mouse_event(0x0040,0,0,0,[UIntPtr]::Zero) }
    } else {
        [Goa2PlayerWindow]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero)
        [System.Threading.Thread]::Sleep(100)
        [Goa2PlayerWindow]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
    }
    [System.Threading.Thread]::Sleep(350)
    Write-Output "$Action player at $X,$Y"
} else {
    if ([Goa2PlayerWindow]::GetForegroundWindow() -ne $goaHandle) { throw "Player is not foreground; capture postponed." }
    $goaBitmap = [System.Drawing.Bitmap]::new($goaRect.Right,$goaRect.Bottom)
    $goaGraphics = [System.Drawing.Graphics]::FromImage($goaBitmap)
    try {
        $goaGraphics.CopyFromScreen($goaPoint.X,$goaPoint.Y,0,0,$goaBitmap.Size)
        $goaTarget = Join-Path $goaRoot "artifacts\unity\$Name.png"
        $goaBitmap.Save($goaTarget)
        Write-Output $goaTarget
    } finally { $goaGraphics.Dispose(); $goaBitmap.Dispose() }
}

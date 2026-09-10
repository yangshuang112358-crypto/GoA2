param(
    [ValidateSet("Capture","Click","Scroll","Key","Drag")][string]$Action = "Capture",
    [int]$X = 0, [int]$Y = 0,
    [string]$Caption,
    [string]$Element,
    [ValidateSet("1","2","3","4","Home","Escape","Tab","Enter")][string]$Key = "1",
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
    $goaKeyCodes = @{ '1'=0x31; '2'=0x32; '3'=0x33; '4'=0x34; Home=0x24; Escape=0x1B; Tab=0x09; Enter=0x0D }
    [Goa2PlayerWindow]::keybd_event($goaKeyCodes[$Key],0,0,[UIntPtr]::Zero)
    [System.Threading.Thread]::Sleep(100)
    [Goa2PlayerWindow]::keybd_event($goaKeyCodes[$Key],0,2,[UIntPtr]::Zero)
    [System.Threading.Thread]::Sleep(350)
    Write-Output "Key $Key sent to player"
} elseif ($Action -ne "Capture") {
    if ($Caption -or $Element) {
        $goaDeadline = [DateTime]::UtcNow.AddSeconds(3)
        do {
            $goaLayout = Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/render-latest.ui.json') -Raw | ConvertFrom-Json
            $goaMatches = @($goaLayout.Buttons | Where-Object { $_.Enabled -and $(if ($Element) { $_.Name -eq $Element } else { $_.Text -match $Caption }) })
            if ($goaMatches.Count -eq 1) { break }
            [System.Threading.Thread]::Sleep(100)
        } while ([DateTime]::UtcNow -lt $goaDeadline)
        if ($goaMatches.Count -ne 1) { throw "Selector must match exactly one enabled button; matched $($goaMatches.Count)." }
        $X = [int]($goaMatches[0].Bounds.x + $goaMatches[0].Bounds.width / 2)
        $Y = [int]($goaMatches[0].Bounds.y + $goaMatches[0].Bounds.height / 2)
    }
    if ([Goa2PlayerWindow]::GetForegroundWindow() -ne $goaHandle) { throw "Player is not foreground; no input sent." }
    if ($X -lt 0 -or $Y -lt 0 -or $X -ge $goaRect.Right -or $Y -ge $goaRect.Bottom) { throw "Point is outside the player." }
    [Goa2PlayerWindow]::SetCursorPos($goaPoint.X + $X, $goaPoint.Y + $Y) | Out-Null
    if ($Action -eq "Scroll") {
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

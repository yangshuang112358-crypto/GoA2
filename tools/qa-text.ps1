param([Parameter(Mandatory)][ValidateLength(1,128)][ValidatePattern('^[^\r\n\t\x00-\x1F]+$')][string]$Text)
$ErrorActionPreference='Stop'
$goaRoot=Split-Path -Parent $PSScriptRoot
$goaPlayer=Get-Process -Id ([int](Get-Content -LiteralPath (Join-Path $goaRoot 'artifacts/unity/player.pid')))
if ($goaPlayer.ProcessName -ne 'Goa2V1' -or $goaPlayer.Path -ne (Join-Path $goaRoot 'artifacts/player/Goa2V1.exe')) { throw 'Text input only targets the tracked QA Player.' }
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class Goa2UnicodeInput {
 [StructLayout(LayoutKind.Sequential)] public struct KEYBOARD { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
 [StructLayout(LayoutKind.Sequential)] public struct MOUSE { public int x,y; public uint data,flags,time; public UIntPtr extra; }
 [StructLayout(LayoutKind.Explicit)] public struct UNION { [FieldOffset(0)] public KEYBOARD keyboard; [FieldOffset(0)] public MOUSE mouse; }
 [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public UNION input; }
 [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,INPUT[] inputs,int size);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
 public static void Send(IntPtr window,string text) {
  foreach(char character in text) {
   if(GetForegroundWindow()!=window) throw new InvalidOperationException("Player lost foreground; text input stopped.");
   var down=new INPUT {type=1,input=new UNION {keyboard=new KEYBOARD {scan=character,flags=4}}};
   var up=new INPUT {type=1,input=new UNION {keyboard=new KEYBOARD {scan=character,flags=6}}};
   if(SendInput(2,new[]{down,up},Marshal.SizeOf(typeof(INPUT)))!=2) throw new Win32Exception(Marshal.GetLastWin32Error());
   System.Threading.Thread.Sleep(40);
  }
 }
}
'@
[Goa2UnicodeInput]::SetForegroundWindow($goaPlayer.MainWindowHandle) | Out-Null
[Goa2UnicodeInput]::Send($goaPlayer.MainWindowHandle,$Text)
Start-Sleep -Milliseconds 350
Write-Output "Typed $($Text.Length) characters into the QA Player."

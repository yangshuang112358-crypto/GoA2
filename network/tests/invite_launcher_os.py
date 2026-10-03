"""Exclusive real OS clicks: create, export, import, launch two Players, stop/save.
Run only when the user is not using keyboard/mouse. Each click verifies foreground.
"""
import ctypes
from ctypes import wintypes as w
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid
from PIL import ImageGrab

user=ctypes.windll.user32;user.SetProcessDPIAware()
user.GetForegroundWindow.restype=w.HWND;user.GetAncestor.restype=w.HWND
callback=ctypes.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
package=Path(sys.argv[1]).resolve();out=Path(sys.argv[2]).resolve();out.mkdir(parents=True,exist_ok=False)
launchers=[];runs=[];checks=[];game_pids=[]
data=Path(os.environ['LOCALAPPDATA'])/'Goa2V1/Multiplayer'
def windows(parent=None):
    found=[]
    @callback
    def collect(hwnd,_):
        pid=w.DWORD();user.GetWindowThreadProcessId(hwnd,ctypes.byref(pid))
        text=ctypes.create_unicode_buffer(1024);user.GetWindowTextW(hwnd,text,len(text))
        if user.IsWindowVisible(hwnd):found.append((hwnd,pid.value,text.value))
        return True
    if parent:user.EnumChildWindows(parent,collect,0)
    else:user.EnumWindows(collect,0)
    return found
def wait(test,seconds=100):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        result=test()
        if result:return result
        time.sleep(.2)
    raise AssertionError('OS test timeout')
def focus(hwnd):
    if user.GetForegroundWindow()!=hwnd:
        user.keybd_event(0x12,0,0,0);user.SetForegroundWindow(hwnd);user.keybd_event(0x12,0,2,0);time.sleep(.2)
    assert user.GetForegroundWindow()==hwnd,'Foreground ownership lost'
def click(hwnd):
    root=user.GetAncestor(hwnd,2);focus(root)
    rect=w.RECT();user.GetWindowRect(hwnd,ctypes.byref(rect))
    user.SetCursorPos((rect.left+rect.right)//2,(rect.top+rect.bottom)//2)
    assert user.GetForegroundWindow()==root,'Foreground changed before click'
    user.mouse_event(2,0,0,0,0);user.mouse_event(4,0,0,0,0);time.sleep(.2)
def button(hwnd,caption):return next(h for h,_,t in windows(hwnd) if t==caption)
def capture(hwnd,name):
    focus(hwnd);time.sleep(.3);rect=w.RECT();user.GetWindowRect(hwnd,ctypes.byref(rect))
    ImageGrab.grab(bbox=(rect.left,rect.top,rect.right,rect.bottom)).save(out/name)
def start():
    p=subprocess.Popen(['powershell.exe','-NoProfile','-STA','-File',str(package/'launcher/Launcher.ps1')],creationflags=0x08000000);launchers.append(p)
    form=wait(lambda:next((h for h,pid,t in windows() if pid==p.pid and t=='Goa2V1 · 邀请好友联机'),None))
    return p,form
def findrun(pid):
    for f in sorted(data.glob('auto-*/request.private.json'),key=lambda x:x.stat().st_mtime,reverse=True):
        try:
            if json.loads(f.read_text(encoding='utf-8-sig'))['ParentPid']==pid:return f.parent
        except (ValueError,OSError):pass
def state(folder):
    try:return json.loads((folder/'state.json').read_text(encoding='utf-8-sig'))
    except (ValueError,OSError):return {}
def findgame(parent):
    code=f"Get-CimInstance Win32_Process -Filter 'ParentProcessId={parent}' | Where-Object Name -eq 'Goa2V1.exe' | Select-Object -ExpandProperty ProcessId"
    text=subprocess.check_output(['powershell.exe','-NoProfile','-Command',code],creationflags=0x08000000).decode().strip()
    return int(text) if text else None
def file_dialog(pid,path):
    dialog=wait(lambda:next((h for h,p,t in windows() if p==pid and t in ('另存为','Save As','打开','Open')),None),10)
    focus(dialog)
    def key(code):user.keybd_event(code,0,0,0);user.keybd_event(code,0,2,0)
    def combo(mod,code):user.keybd_event(mod,0,0,0);key(code);user.keybd_event(mod,0,2,0)
    combo(0x12,ord('N'));combo(0x11,ord('A'))
    for char in str(path):
        assert ord(char)<128,'OS test path must be ASCII'
        v=user.VkKeyScanW(ord(char));assert v!=-1
        if v & 0x100:user.keybd_event(0x10,0,0,0)
        key(v&255)
        if v & 0x100:user.keybd_event(0x10,0,2,0)
    key(0x0D)
def check(name,condition=True):
    assert condition,name;checks.append(name);print('PASS',name,flush=True)
try:
    hp,hf=start();capture(hf,'01-launcher.png');check('visible automatic launcher')
    click(button(hf,'创建房间并进入'));hr=wait(lambda:findrun(hp.pid));runs.append(hr)
    wait(lambda:state(hr).get('Phase')=='Ready');check('OS create starts managed public transport')
    hgame=wait(lambda:findgame(hp.pid));game_pids.append(hgame)
    hw=wait(lambda:next((h for h,p,t in windows() if p==hgame and 'Goa2V1' in t),None));time.sleep(4)
    capture(hw,'02-host-player.png');check('host real Player opens')
    capture(hf,'03-host-ready.png')
    invitation=out/'friend.private.json'
    click(button(hf,'导出这个好友的邀请'));file_dialog(hp.pid,invitation)
    wait(lambda:invitation.exists());check('OS save dialog exported unified seat invitation',json.loads(invitation.read_text(encoding='utf-8-sig'))['Seat']==1)
    jp,jf=start();click(button(jf,'导入邀请并加入'));file_dialog(jp.pid,invitation)
    jr=wait(lambda:findrun(jp.pid));runs.append(jr);wait(lambda:state(jr).get('Phase')=='Ready')
    check('OS import finds host and authenticates seat',state(jr)['Seat']==1)
    jgame=wait(lambda:findgame(jp.pid));game_pids.append(jgame)
    jw=wait(lambda:next((h for h,p,t in windows() if p==jgame and 'Goa2V1' in t),None));time.sleep(4)
    capture(jw,'04-friend-player.png');capture(jf,'05-friend-ready.png');check('friend real Player opens')
    user.PostMessageW(jw,0x10,0,0);user.PostMessageW(hw,0x10,0,0);time.sleep(2)
    click(button(jf,'取消／停止本次连接'));wait(lambda:state(jr).get('Phase')=='Stopped');check('OS guest disconnect')
    click(button(hf,'取消／停止本次连接'))
    dialog=wait(lambda:next((h for h,p,t in windows() if p==hp.pid and t=='停止房间'),None),10)
    yes=next(h for h,_,t in windows(dialog) if t.startswith('是') or t in ('Yes','&Yes'))
    click(yes);wait(lambda:state(hr).get('Phase')=='Stopped');check('OS host stop verifies save')
    capture(hf,'06-saved.png')
    for p,f in ((jp,jf),(hp,hf)):user.PostMessageW(f,0x10,0,0);p.wait(timeout=10)
    check('both launchers close')
    (out/'report.json').write_text(json.dumps({'passed':True,'checks':checks,'mode':'two real Players and OS mouse/keyboard; same PC public bootstrap; no cross-machine claim'},indent=2),encoding='utf-8')
finally:
    for folder in runs:(folder/'stop.request').write_text('stop')
    for h,p,t in windows():
        if p in game_pids:user.PostMessageW(h,0x10,0,0)
    for p in launchers:
        if p.poll() is None:
            # Stop owned worker gracefully before closing the test UI.
            time.sleep(3)
            for h,pid,t in windows():
                if pid==p.pid and t.startswith('Goa2V1'):user.PostMessageW(h,0x10,0,0)

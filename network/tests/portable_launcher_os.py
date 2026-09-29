"""Exclusive visible OS-input smoke test for the packaged Windows launcher."""
import ctypes
from ctypes import wintypes as w
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time

from PIL import ImageGrab

user = ctypes.windll.user32
user.SetProcessDPIAware()
user.GetForegroundWindow.restype = w.HWND
CALLBACK = ctypes.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
package = Path(sys.argv[1]).resolve()
output = Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=False)
checks = []
process = subprocess.Popen(["powershell.exe", "-NoProfile", "-STA", "-File", str(package / "launcher/Launcher.ps1")], creationflags=subprocess.CREATE_NO_WINDOW)


def windows(parent=None):
    items = []

    @CALLBACK
    def collect(hwnd, _):
        pid = w.DWORD()
        user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        text = ctypes.create_unicode_buffer(1024)
        user.GetWindowTextW(hwnd, text, len(text))
        if user.IsWindowVisible(hwnd):
            items.append((hwnd, pid.value, text.value))
        return True

    if parent:
        user.EnumChildWindows(parent, collect, 0)
    else:
        user.EnumWindows(collect, 0)
    return items


def wait(predicate, seconds=20):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = predicate()
        if result:
            return result
        time.sleep(.15)
    raise AssertionError("Timed out")


def child_process(name):
    command = "Get-CimInstance Win32_Process -Filter 'ParentProcessId=" + str(process.pid) + "' | Select-Object ProcessId,Name,CommandLine | ConvertTo-Json -Compress"
    value = subprocess.check_output(["powershell.exe", "-NoProfile", "-Command", command], creationflags=subprocess.CREATE_NO_WINDOW)
    if not value.strip():
        return None
    items = json.loads(value)
    if isinstance(items, dict):
        items = [items]
    return next((item for item in items if item["Name"] == name), None)


def focus(hwnd):
    if user.GetForegroundWindow() == hwnd:
        return
    user.keybd_event(0x12, 0, 0, 0)
    user.SetForegroundWindow(hwnd)
    user.keybd_event(0x12, 0, 2, 0)
    time.sleep(.2)
    assert user.GetForegroundWindow() == hwnd, "Could not acquire exclusive foreground"


def key(code):
    user.keybd_event(code, 0, 0, 0)
    user.keybd_event(code, 0, 2, 0)


def click(hwnd):
    rect = w.RECT()
    user.GetWindowRect(hwnd, ctypes.byref(rect))
    user.SetCursorPos((rect.left + rect.right)//2, (rect.top + rect.bottom)//2)
    user.mouse_event(2, 0, 0, 0, 0)
    user.mouse_event(4, 0, 0, 0, 0)
    time.sleep(.2)


def button(form, caption):
    return next(hwnd for hwnd, _, text in windows(form) if text == caption)


def capture(hwnd, filename):
    focus(hwnd)
    rect = w.RECT()
    user.GetWindowRect(hwnd, ctypes.byref(rect))
    ImageGrab.grab(bbox=(rect.left, rect.top, rect.right, rect.bottom)).save(output / filename)


new_players = []
new_room = None
try:
    form = wait(lambda: next((h for h, pid, title in windows() if pid == process.pid and title.startswith("Goa2V1")), None))
    capture(form, "launcher.png")
    checks.append("visible Chinese launcher")
    # The address combobox is the first combobox in this form; choose its final
    # explicitly labelled loopback item rather than altering real network settings.
    combo = None
    for hwnd, _, _ in windows(form):
        name = ctypes.create_unicode_buffer(256)
        user.GetClassNameW(hwnd, name, len(name))
        if "COMBOBOX" in name.value.upper():
            combo = hwnd
            break
    assert combo
    click(combo)
    key(0x23)  # End
    key(0x0D)
    click(button(form, "创建房间"))
    child = wait(lambda: child_process("Goa2.Network.exe"))
    new_room = Path(re.findall(r'"([^\"]*)"', child["CommandLine"])[-2]).parent
    wait(lambda: (new_room / "private/ready.json").exists())
    player = wait(lambda: child_process("Goa2V1.exe"))
    new_players.append(player)
    wait(lambda: any(pid == player['ProcessId'] for _, pid, _ in windows()))
    time.sleep(1)  # Let the newly opened Unity window finish taking foreground.
    checks.append("OS click created portable server and host Player")
    wait(lambda: any("房间运行中" in text for _, _, text in windows(form)))
    capture(form, "hosting.png")
    focus(form)
    click(button(form, "停止房间并保存"))
    dialog = wait(lambda: next((h for h, pid, title in windows() if pid == process.pid and title == "停止房间"), None))
    focus(dialog)
    yes = next(h for h, _, text in windows(dialog) if text.startswith('是') or text in ('&Yes', 'Yes'))
    click(yes)  # Explicit confirmation of stopping this test's room.
    wait(lambda: (new_room / "private/restore-check.json").exists())
    wait(lambda: any("已停止，存档导出且检查通过" in text for _, _, text in windows(form)))
    checks.append("OS confirmation stopped room with verified save")
    capture(form, "stopped.png")
    focus(form)
    user.keybd_event(0x12, 0, 0, 0)
    key(0x73)  # Alt+F4
    user.keybd_event(0x12, 0, 2, 0)
    assert process.wait(timeout=10) == 0
    checks.append("launcher closed without live room")
    (output / "report.json").write_text(json.dumps({"passed": True, "runner": "visible WinForms; real Windows mouse/keyboard",
        "checks": checks, "cross_machine": False, "player_gameplay_tested": False}, indent=2), encoding="utf-8")
    print(json.dumps({"passed": True, "checks": len(checks), "evidence": str(output)}))
finally:
    # Clean up only this invocation's child windows; leave existing user games alone.
    if new_room and not (new_room / "private/restore-check.json").exists():
        (new_room / "private/stop.request").write_text("stop")
    for child in new_players:
        for hwnd, pid, _ in windows():
            if pid == child["ProcessId"]:
                user.PostMessageW(hwnd, 0x10, 0, 0)
    if process.poll() is None:
        for hwnd, pid, title in windows():
            if pid == process.pid and title.startswith("Goa2V1"):
                user.PostMessageW(hwnd, 0x10, 0, 0)

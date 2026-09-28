"""Four real Windows Players + TCP authority, synthetic UI actions (not OS input)."""
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import traceback
from concurrent.futures import ThreadPoolExecutor
from acceptance import Run, ROOT


class UnityRun(Run):
    def __init__(self, fixture=None, steps=0, faults=False):
        super().__init__(fixture, steps, faults=faults)
        for process in self.clients:
            process.terminate()
            process.wait(timeout=10)
        self.clients = []
        self.seq = [0]*4
        self.report['runner'] = '4 Windows Unity Players + TCP service; synthetic GameScreen callbacks, NOT OS input'
        self.report['ui_tested'] = True
        exe = ROOT/'artifacts/player/Goa2V1.exe'
        self.report['player_build_info_sha256'] = hashlib.sha256((exe.parent/'build-info.json').read_bytes()).hexdigest()
        self.report['player_source_files'] = json.loads((exe.parent/'build-info.json').read_text(encoding='utf-8-sig'))['SourceFiles']
        for seat in range(4):
            folder = self.output/f'ui-{seat}'
            folder.mkdir()
            ticket = self.output/f'private/seat-{seat}.private.json'
            if faults and seat in (0,1):
                ticket = self.output/f'private/proxy-{seat}.private.json'
            startup = subprocess.STARTUPINFO()
            startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startup.wShowWindow = 0
            self.clients.append(subprocess.Popen([str(exe), '-goaNetworkTicket', str(ticket),
                '-goaNetworkAudit',str(folder),'-screen-fullscreen','0','-screen-width','1600','-screen-height','1000',
                '-logFile',str(folder/'player.log')],startupinfo=startup))
        self.report['client_pids'] = [p.pid for p in self.clients]

    def ui(self, actor, op, **args):
        seat = actor
        folder = self.output/f'ui-{seat}'
        if op=='screen' and '--capture-visible' in sys.argv:
            callback=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HWND,wintypes.LPARAM)
            def show(hwnd,_):
                pid=wintypes.DWORD();ctypes.windll.user32.GetWindowThreadProcessId(hwnd,ctypes.byref(pid))
                if pid.value==self.clients[seat].pid:ctypes.windll.user32.ShowWindowAsync(hwnd,4)
                return True
            ctypes.windll.user32.EnumWindows(callback(show),0)
            time.sleep(1)
        self.seq[seat] += 1
        message = dict(seq=self.seq[seat],op=op,**args)
        temp = folder/'input.tmp'
        temp.write_text(json.dumps(message,ensure_ascii=False),encoding='utf-8')
        write_deadline=time.monotonic()+3
        while True:
            try:
                temp.replace(folder/'input.json');break
            except PermissionError:
                if time.monotonic()>=write_deadline:raise
                time.sleep(.01)
        deadline = time.monotonic()+45
        while time.monotonic()<deadline:
            if self.clients[seat].poll() is not None:
                raise RuntimeError(f'Player {seat} exited; inspect its log')
            try:
                result=json.loads((folder/f'result-{self.seq[seat]}.json').read_text(encoding='utf-8'))
                if result['seq']==message['seq']:
                    assert not result['error'], result['error']
                    return result
            except (FileNotFoundError, json.JSONDecodeError, PermissionError):
                pass
            time.sleep(.05)
        raise TimeoutError(f'Player {seat} operation {op}')

    def rpc(self, seat, op, **values):
        if op=='view': return self.ui(seat,'view',**values)['view']
        data=self.ui(seat,op,**values)
        if op in ('submit','retry','pick','confirm'):
            return {**(data['result'] or {}),'Snapshot':data['view']}
        return data

    def start_checks(self):
        for seat in range(4):
            data=self.ui(seat,'view')
            self.check(f'Player {seat} authenticated own seat and no local rules session',data['seat']==seat and not data['hasLocalSession'] and data['hasBoard'])
            changed=self.ui(seat,'switch',seat=(seat+1)%4)
            self.check(f'number key identity fixed {seat}',changed['seat']==seat)
        self.revision=self.view()['Revision']

    def ordinary(self):
        self.start_checks()
        # Proxy drops first result and closes seat 0. UI must retain original ID.
        first=self.ui(0,'submit',kind='ChooseHero',args={'Value':'wasp'})
        self.check('lost result marks UI uncertain and blocks new actions',first['uncertain'] and not first['canAct'])
        self.ui(0,'connect')
        result=self.ui(0,'retry')
        self.check('UI retry preserves command ID and does not duplicate',result['result']['Duplicate'] and not result['uncertain'])
        self.revision=result['view']['Revision']
        with ThreadPoolExecutor(1) as pool:
            delayed=pool.submit(self.ui,1,'submit',kind='ChooseHero',args={'Value':'shargatha'})
            self.rpc(2,'view',revision=2);self.revision=2
            self.command(2,'ChooseHero',Value='brogan')
            response=delayed.result(timeout=20)
        self.check('delayed reply cannot rewind Unity view',response['result']['Accepted'] and self.view(1)['Revision']==3)
        self.command(3,'ChooseHero',Value='arien')
        while self.view()['Phase']=='Deployment':
            for seat in range(4):
                choices=self.view(seat)['Deployments']
                if choices:
                    target,cells=next(iter(choices.items()))
                    self.command(seat,'DeployHero',TargetSeat=int(target),Destination=cells[0])
                    break
            else: raise AssertionError('deployment stalled')
        self.check('network requires four confirmations',not self.view()['QuickSelection'] and not self.view()['Sandbox'])
        for turn in range(1,5):
            for seat in range(4):
                self.ui(seat,'switch',seat=seat)  # open own wheel (switch never changes identity)
                own=self.view(seat)
                card=next(c for c in own['OwnCards'] if c['Zone']=='InHand')['CardId']
                # Direct submit still traverses GameScreen -> shared UI intent -> service.
                self.command(seat,'SelectCard',Value=card)
                if turn==1 and seat==0:
                    self.command(seat,'CancelCardSelection')
                    self.check('cancel selection routed over TCP',not any(c['Zone']=='Selected' for c in self.view(seat)['OwnCards']))
                    self.command(seat,'SelectCard',Value=card)
                    for other in (1,2,3):
                        self.check(f'public skills known but choice hidden {other}',all(c['Zone']!='Selected' for c in self.view(other)['Players'][0]['PublicCards']))
                    self.ui(seat,'disconnect')
                    frozen=self.ui(seat,'view')
                    self.check('disconnect disables map and confirmation',not frozen['canAct'] and not frozen['boardConnected'] and not frozen['confirmEnabled'])
                    self.ui(seat,'connect')
                    self.check('reconnect preserves selected card',any(c['Zone']=='Selected' for c in self.view(seat)['OwnCards']))
                self.command(seat,'ConfirmCard')
                if seat<3:self.check(f'wait for all players {turn}/{seat}',self.view()['Phase']=='Planning')
            for _ in range(30):
                view=self.view()
                if view['Phase']=='InitiativeChoice':
                    chooser=view['Pending']['ChooserSeat'];own=self.view(chooser)
                    self.command(chooser,'ChooseInitiative',TargetSeat=own['Pending']['CandidateSeats'][0])
                elif view['Phase']=='Action':self.command(view['ActiveSeat'],'Pass')
                else:break
            self.check(f'round turn {turn} complete',self.view()['Phase']==('RoundEnd' if turn==4 else 'Planning'))
        self.command(0,'ResolveRoundEnd')
        self.check('Unity clients complete full round',self.view()['Round']==2)
        public=lambda v:{k:v[k] for k in ('Revision','Phase','Players','Units','BlueCrystal','RedCrystal','Round')}
        self.check('all four rendered projections agree',all(public(self.view(i))==public(self.view()) for i in range(4)))
        self.ui(0,'screen')

    def pending_ui(self):
        self.start_checks()
        self.ui(0,'switch',seat=0)
        before=self.view(0)['Revision']
        selected=self.ui(0,'pick',card='brogan-00-猛攻')
        self.check('discard preview stays local without command',selected['wheelPreview']=='brogan-00-猛攻' and selected['view']['Revision']==before)
        self.check('other client sees no preview',self.ui(1,'view')['wheelPreview']=='')
        self.ui(0,'disconnect');self.ui(0,'connect')
        self.check('reconnect clears tentative discard',self.ui(0,'view')['wheelPreview']=='')
        self.ui(0,'pick',card='brogan-00-猛攻')
        confirmed=self.ui(0,'confirm');self.revision=confirmed['view']['Revision']
        self.check('UI confirm submits discard exactly once',confirmed['result']['Accepted'] and self.revision==before+1)
        for seat in range(4):
            self.check(f'public discard event delivered {seat}',any(e['Kind']=='DiscardColorShown' for e in self.view(seat)['Events']))
        time.sleep(3)
        self.command(0,'ChooseAttackTarget',Value='hero:1')
        self.reconnect_pending(1,'defense','DefenseOptions')
        self.command(1,'Defend',Value='wasp-10-反射屏障')
        self.reconnect_pending(0,'forced_discard','ForcedDiscardCards')
        time.sleep(3)
        self.ui(0,'pick',card='brogan-06-铜墙铁壁');result=self.ui(0,'confirm');self.revision=result['view']['Revision']
        self.check('nested attack defense discard resumes parent in Unity',self.view()['ActiveSeat']==3)
        self.ui(0,'screen')

    def reconnect_pending(self, seat, kind, candidates):
        before=self.view(seat);self.ui(seat,'disconnect');self.ui(seat,'connect');after=self.view(seat)
        self.check('Unity reconnect '+kind,before['Pending']['Kind']==kind and after['Pending']==before['Pending'] and after[candidates]==before[candidates])
        for other in range(4):
            if other!=seat:self.check(f'private {candidates} seat {other}',not self.view(other)[candidates])

    def capture_ui(self):
        self.start_checks();self.ui(0,'switch',seat=0);self.ui(0,'screen');time.sleep(2)
        from PIL import Image
        image=Image.open(self.output/'ui-0/screen.png').convert('RGB')
        self.check('network game screen contains rendered UI',max(channel[1]-channel[0] for channel in image.getextrema())>80)

    def finish(self):
        for seat in range(len(self.clients)):
            try:self.ui(seat,'quit')
            except Exception:pass
        # Parent cleanup handles service Restore, projections and credential log scan.
        super().finish()


if __name__=='__main__':
    cases=[(None,0,True,'ordinary'),('tests/scenarios/throwing-axe-reflection.json',11,False,'pending_ui')]
    if '--pending-only' in sys.argv:cases=cases[1:]
    if '--capture-only' in sys.argv:cases=[('tests/scenarios/throwing-axe-reflection.json',11,False,'capture_ui')]
    for fixture,steps,faults,method in cases:
        run=UnityRun(fixture,steps,faults)
        try:
            getattr(run,method)();run.report['passed']=True
        except Exception:
            run.report['passed']=False;run.report['failure']=traceback.format_exc();raise
        finally:run.finish()

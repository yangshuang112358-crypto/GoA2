"""Real four-seat authority through four managed, unprivileged EasyTier cores.
Local relay mode is reproducible. --public uses the production bootstrap peers;
neither mode claims separate-computer or China ISP acceptance.
"""
import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import threading
import time
import uuid
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'client'))
from player import Player

def port():
    with socket.socket() as s: s.bind(('127.0.0.1',0)); return s.getsockname()[1]

def run(package, public=False, relay_only=False):
    root=Path(__file__).resolve().parents[2]
    out=root/'artifacts/network'/('automatic-'+uuid.uuid4().hex);out.mkdir()
    checks=[];workers=[];players=[];logs=[];relay=None
    started=subprocess.check_output(['powershell.exe','-NoProfile','-Command',f'(Get-Process -Id {os.getpid()}).StartTime.ToUniversalTime().Ticks.ToString()'],text=True).strip()
    def check(name,value):
        if not value:raise AssertionError(name)
        checks.append(name);print('PASS',name,flush=True)
    def start(mode,invite=None,peers=None):
        folder=out/('run-'+uuid.uuid4().hex);folder.mkdir()
        request={'Mode':mode,'Package':str(package),'Peers':peers or [],'RelayOnly':relay_only,'ParentPid':os.getpid(),'ParentStarted':started}
        (folder/'request.private.json').write_text(json.dumps(request),encoding='utf-8')
        if invite:(folder/'invitation.private.json').write_bytes(invite.read_bytes())
        log=open(folder/'worker.log','wb');logs.append(log)
        p=subprocess.Popen(['powershell.exe','-NoProfile','-File',str(package/'launcher/BootstrapWorker.ps1'),'-Run',str(folder)],stdout=log,stderr=log,creationflags=0x08000000)
        workers.append((p,folder));return p,folder
    def state(item):
        p,folder=item
        try:return json.loads((folder/'state.json').read_text(encoding='utf-8-sig'))
        except (OSError,ValueError):return {}
    def wait(item,phases=('Ready',),seconds=100):
        deadline=time.monotonic()+seconds
        while time.monotonic()<deadline:
            v=state(item)
            if v.get('Phase') in phases:return v
            if v.get('Phase')=='Error' or item[0].poll() is not None:raise RuntimeError('Bootstrap failed: '+str(v))
            time.sleep(.2)
        raise TimeoutError('Bootstrap timeout: '+str(state(item)))
    def stop(item):
        (item[1]/'stop.request').write_text('stop')
        item[0].wait(timeout=30)
        return state(item)
    try:
        if public:peers=['tcp://38.147.105.185:11010']
        else:
            relay_port=port();peers=[f'tcp://127.0.0.1:{relay_port}']
            log=open(out/'relay.log','wb');logs.append(log)
            relay=subprocess.Popen([str(package/'easytier/easytier-core.exe'),'--network-name','Goa2-test-relay','--network-secret',uuid.uuid4().hex,'--listeners',peers[0],'--rpc-portal',f'127.0.0.1:{port()}','--no-tun','true','--bind-device','false','--disable-upnp','true'],stdout=log,stderr=log,creationflags=0x08000000)
            time.sleep(1)
        host=start('Host',peers=peers);hs=wait(host)
        check('host automatic network ready',hs['HostPid']>0 and hs['CorePid']>0)
        invitations=sorted(Path(hs['Invitations']).glob('*.json'))
        check('three separate seat invitations',len(invitations)==3 and len({json.loads(p.read_text())['Ticket']['Credential'] for p in invitations})==3)
        guests=[start('Join',invite=p) for p in invitations]
        gs=[wait(g) for g in guests]
        check('all three guests authenticate',sorted(v['Seat'] for v in gs)==[1,2,3])
        for seat,v in enumerate([hs]+gs):
            ticket=json.loads(Path(v['Ticket']).read_text())
            client=Player(ticket);players.append(client);client.connect()
            check(f'actual TCP seat {seat}',client.seat==seat)
        revision=0
        for seat,hero in enumerate(('wasp','sabina','tigerclaw','arien')):
            players[seat].wait(lambda:players[seat].view['Revision']>=revision)
            result=players[seat].submit('ChooseHero',{'Value':hero})
            check(f'authority accepts seat {seat} hero',result.get('Accepted') is True)
            revision=result['Snapshot']['Revision']
        for seat,p in enumerate(players):
            p.wait(lambda:p.view['Revision']==revision)
            check(f'projection agrees seat {seat}',[x['HeroId'] for x in p.view['Players']]==['wasp','sabina','tigerclaw','arien'])
        players[2].disconnect();players[2].connect()
        check('client reconnect through forwarding',players[2].seat==2 and players[2].view['Revision']==revision)
        duplicate=start('Join',invite=invitations[0]);ds=wait(duplicate,('Error',))
        check('same-seat duplicate worker rejected',ds['Phase']=='Error')
        # New unrelated local listener must not be reachable via host's virtual IP.
        sentinel=socket.socket();sentinel.bind(('127.0.0.1',0));sentinel.listen();sentinel.settimeout(3)
        denied_port=sentinel.getsockname()[1];probe_port=port()
        cli=package/'easytier/easytier-cli.exe'
        result=subprocess.run([str(cli),'-p',f"127.0.0.1:{gs[0]['RpcPort']}",'port-forward','add','tcp',f'127.0.0.1:{probe_port}',f'10.233.42.1:{denied_port}'],capture_output=True,timeout=5)
        check('ACL probe forwarding configured',result.returncode==0)
        with socket.create_connection(('127.0.0.1',probe_port),2) as probe:
            probe.sendall(b'not a game port')
            try:
                unexpected,_=sentinel.accept();unexpected.close();denied=False
            except socket.timeout:denied=True
        check('host ACL blocks unrelated local TCP service',denied)
        sentinel.close()
        for p in players:p.disconnect()
        oldcore=gs[0]['CorePid'];ss=stop(guests[0]);check('guest stop releases only its connection',ss['Phase']=='Stopped' and host[0].poll() is None)
        rejoined=start('Join',invite=invitations[0]);rs=wait(rejoined);p=Player(json.loads(Path(rs['Ticket']).read_text()));players.append(p);p.connect()
        check('saved invitation reconnects same seat/revision',p.seat==1 and p.view['Revision']==revision);p.disconnect()
        cancelled=start('Host',peers=['tcp://127.0.0.1:'+str(port())]);wait(cancelled,('Connecting',))
        cs=stop(cancelled);check('cancel unreachable network saves room',cs['Phase']=='Stopped')
        bad=json.loads(invitations[1].read_text());bad['Ticket']['Capabilities']['EngineVersion']=-1
        badpath=out/'bad.private.json';badpath.write_text(json.dumps(bad));badworker=start('Join',invite=badpath);bs=wait(badworker,('Error',))
        check('version rejection before starting network',bs['CorePid']==0)
        # Abruptly terminate a guest worker: its Windows Job must kill its core only.
        crashed_core=rs['CorePid'];rejoined[0].kill();rejoined[0].wait();time.sleep(.5)
        alive=subprocess.run(['powershell.exe','-NoProfile','-Command',f'if(Get-Process -Id {crashed_core} -ErrorAction SilentlyContinue){{exit 1}}'],capture_output=True).returncode
        check('abnormal worker exit leaves no owned core',alive==0 and host[0].poll() is None)
        ss=stop(host);check('host graceful stop validates save',ss['Phase']=='Stopped' and json.loads((Path(hs['RoomPath'])/'private/restore-check.json').read_text())['passed'])
        report={'passed':True,'mode':'same PC public bootstrap' if public else 'same PC local relay','relay_only':relay_only,'checks':checks,'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()}
        (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        print('REPORT',out/'report.json',flush=True)
    finally:
        for p in players:p.disconnect()
        for item in workers:
            if item[0].poll() is None:
                try:stop(item)
                except Exception:item[0].kill();item[0].wait()
        if relay:relay.terminate();relay.wait()
        for f in logs:f.close()

if __name__=='__main__':
    a=argparse.ArgumentParser();a.add_argument('package',type=Path);a.add_argument('--public',action='store_true');a.add_argument('--relay-only',action='store_true');v=a.parse_args();run(v.package.resolve(),v.public,v.relay_only)

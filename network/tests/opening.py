"""Real TCP service, four authenticated clients; no Unity/OS or four-machine claim."""
import json, os, subprocess, sys, time, uuid, hashlib
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'network/client'))
from player import Player

class OpeningPlayer(Player):
    def __init__(self,ticket):
        super().__init__(ticket);self.motion=None;self.present=[]
    def accept(self,message):
        super().accept(message)
        if message['Type']=='CoinMotion': self.motion=message['Frame']
        if message['Type']=='Welcome':
            self.motion=message.get('CoinMotion');self.present=message['Snapshot']['ConnectedSeats']
        if message['Type']=='Presence':self.present=message['ConnectedSeats']

def main():
    out=ROOT/'artifacts/network'/('opening-'+time.strftime('%Y%m%d-%H%M%S'));out.mkdir(parents=True)
    dotnet=Path(os.environ['LOCALAPPDATA'])/'Goa2V1Toolchain/dotnet/dotnet.exe'
    dll=ROOT/'network/Goa2.Network/bin/Release/net10.0/Goa2.Network.dll'
    checks=[];players=[];log=open(out/'server.log','w',encoding='utf8')
    proc=subprocess.Popen([str(dotnet),str(dll),'serve',str(ROOT),str(out/'private')],stdin=subprocess.PIPE,stdout=log,stderr=log,text=True,creationflags=subprocess.CREATE_NO_WINDOW)
    def check(value,name):
        assert value,name
        checks.append(name)
    def sync():
        rev=max(p.view['Revision'] for p in players)
        for p in players:
            if p.state=='Connected':p.wait(lambda p=p:p.view['Revision']>=rev)
    def command(seat,kind,value,accepted=True):
        sync();r=players[seat].submit(kind,{'Value':value});check(r.get('Accepted',False)==accepted,kind+' '+str(seat)+' '+r.get('Code',''));sync();return r
    def frame(toss,seq=1,elapsed=1,**fields):
        return dict(TossId=toss,Sequence=seq,Time=elapsed,Position=[0,.12,0],Rotation=[0,0,0,1],Velocity=[0,0,0],AngularVelocity=[0,0,0],**fields)
    def motion(f,seat=0,error=None):
        p=players[seat];before=len(p.errors);p.send({'Type':'CoinMotion','Frame':f})
        if error:p.wait(lambda:len(p.errors)>before);check(p.errors[-1]==error,'reject motion '+error)
        else:players[1].wait(lambda:players[1].motion==f)
    try:
        end=time.monotonic()+15
        while not (out/'private/ready.json').exists():
            if proc.poll() is not None or time.monotonic()>end:raise RuntimeError('server start failed')
            time.sleep(.05)
        for i in range(4):
            p=OpeningPlayer(json.loads((out/f'private/seat-{i}.private.json').read_text()));p.connect();players.append(p)
        players[0].wait(lambda:players[0].present==[0,1,2,3]);check(True,'presence reaches all four')
        motion(frame('draft:1'),seat=1,error='stale_coin_motion')
        invalid=frame('draft:1');invalid['Position']=[0,999,0];motion(invalid,error='invalid_coin_motion')
        invalid['Position']='bad';motion(invalid,error='invalid_coin_motion')
        motion(frame('draft:1'));motion(frame('draft:1'),error='stale_coin_motion')
        players[0].disconnect();players[1].wait(lambda:0 not in players[1].present);players[0].connect()
        check(players[0].motion['TossId']=='draft:1','host resume retains original toss and frame')
        command(1,'ReportCoinToss','draft:1|Red|0,0,0,1',False)
        r=command(0,'ReportCoinToss','draft:1|Red|0,0,0,1')
        # Same accepted ID remains idempotent even after transport settlement.
        duplicate=players[0].submit('ReportCoinToss',{'Value':'draft:1|Red|0,0,0,1'},command_id=r['CommandId'],revision=r['Snapshot']['Revision']-1)
        check(duplicate['Duplicate'],'accepted result duplicate is idempotent')
        command(0,'ChooseHero','wasp',False)
        command(1,'ChooseHero','wasp')
        sync()
        with ThreadPoolExecutor(max_workers=2) as pool:
            attempts={s:pool.submit(players[s].submit,'ChooseHero',{'Value':'shargatha'}) for s in (0,2)}
            outcomes={s:f.result(timeout=10) for s,f in attempts.items()}
        winners=[s for s,r in outcomes.items() if r.get('Accepted')]
        check(len(winners)==1,'same-team concurrent claims accept exactly one hero owner')
        sync();loser=2-winners[0]
        command(loser,'ChooseHero','shargatha',False)
        command(loser,'ChooseHero','brogan');command(3,'ChooseHero','arien')
        check(all(p.view['Opening']['DraftComplete'] for p in players),'1-2-1 consistent on all clients')
        for captain in [0,1]:
            while players[captain].view['Deployments']:
                target,cells=next(iter(players[captain].view['Deployments'].items()))
                r=players[captain].submit('DeployHero',{'TargetSeat':int(target),'Destination':cells[0]});check(r['Accepted'],'deploy '+target);sync()
        toss=players[0].view['Opening']['TossId'];check(toss=='opening:2','second physical toss begins after deployment')
        motion(frame(toss,elapsed=18));command(0,'MarkCoinStuck',toss)
        for seat in [0,1,2]:command(seat,'VoteCoinReroll',toss)
        players[2].disconnect();players[3].wait(lambda:2 not in players[3].present)
        command(3,'VoteCoinReroll',toss,False);players[2].connect()
        check(players[2].view['Opening']['RerollVotes']==[0,1,2],'stuck reconnect preserves three votes')
        players[0].wait(lambda:len(players[0].present)==4);command(3,'VoteCoinReroll',toss)
        check(players[0].view['Opening']['TossId']=='opening:3','fourth distinct vote rerolls with new ID')
        motion(frame(toss,seq=2,elapsed=18.1),error='stale_coin_motion')
        motion(frame('opening:3'));command(0,'ReportCoinToss','opening:3|Red|0,0,0,1')
        check(all(p.view['Phase']=='Planning' for p in players),'all clients enter planning after accepted second toss')
        print(json.dumps({'passed':len(checks),'report':str(out/'report.json')},ensure_ascii=False))
    finally:
        for p in players:p.disconnect()
        proc.stdin.write('stop\n');proc.stdin.flush()
        try:proc.wait(timeout=12)
        except subprocess.TimeoutExpired:proc.terminate();proc.wait()
        log.close()
        if (out/'private/coin-trajectories.json').exists():
            trajectory=json.loads((out/'private/coin-trajectories.json').read_text())
            check({f['TossId'] for f in trajectory['Frames']}=={'draft:1','opening:2','opening:3'},'normal stop exports accepted motion trajectories for both purposes and reroll')
        (out/'report.json').write_text(json.dumps({'checks':checks,'assembly_sha256':hashlib.sha256(dll.read_bytes()).hexdigest(),'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'working_tree':subprocess.check_output(['git','diff','--stat'],cwd=ROOT,text=True),'ui_tested':False,'remote_four_machines':False},ensure_ascii=False,indent=2),encoding='utf8')
if __name__=='__main__':main()

"""Four real local Unity clients; only seat zero simulates the physical toss."""
import time,traceback
from unity_acceptance import UnityRun

run=UnityRun(physical=True)
try:
    run.start_checks()
    def settled(purpose):
        deadline=time.monotonic()+100
        while time.monotonic()<deadline:
            v=run.view();opening=v['Opening']
            if opening['Purpose']==purpose and opening['Status']=='settled':
                run.revision=v['Revision'];return v
            if opening['Status']=='stuck':
                for seat in range(4):
                    if seat not in run.view(seat)['Opening']['RerollVotes']:
                        run.command(seat,'VoteCoinReroll',Value=opening['TossId'])
            time.sleep(.5)
        raise TimeoutError(purpose+' physical toss')
    v=settled('draft')
    run.check('four real Unity clients receive same draft physical result',all(run.view(s)['Opening']['Result']==v['Opening']['Result'] for s in range(4)))
    run.ui(0,'disconnect');run.ui(0,'connect')
    run.check('host reconnect after settlement keeps original toss',run.view()['Opening']['TossId']==v['Opening']['TossId'])
    time.sleep(7)
    first=0 if v['DraftTeam']=='Blue' else 1
    for seat,hero in zip([first,1-first,3-first,first+2],['wasp','shargatha','brogan','arien']):
        run.command(seat,'ChooseHero',Value=hero)
        time.sleep(1)
    for captain in (0,1):
        while run.view(captain)['Deployments']:
            target,cells=next(iter(run.view(captain)['Deployments'].items()))
            run.command(captain,'DeployHero',TargetSeat=int(target),Destination=cells[0])
    v=settled('opening');time.sleep(9)
    run.check('four real Unity clients reach same planning state',all(run.view(s)['Phase']=='Planning' and run.view(s)['DecisionCoin']==v['DecisionCoin'] for s in range(4)))
    run.check('all original seat identities preserved',all(run.ui(s,'view')['seat']==s for s in range(4)))
    run.ui(0,'screen')
    run.report['passed']=True
except Exception:
    run.report['error']=traceback.format_exc();raise
finally:
    run.finish()

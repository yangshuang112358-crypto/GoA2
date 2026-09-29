"""Four actual Unity Players; synthetic UI calls, not OS input."""
import traceback
from unity_acceptance import UnityRun

run=UnityRun('tests/scenarios/combat-defense.json',7)
try:
    run.start_checks()
    before=run.view(0);run.revision=before['Revision']
    picked=run.ui(0,'action',value='primary')
    run.check('UI preview has no network revision',picked['actionChoice']=='primary' and picked['view']['Revision']==run.revision)
    run.check('other player cannot see local preview',run.ui(1,'view')['actionChoice']=='')
    returned=run.ui(0,'return')
    run.check('return has no network command',returned['actionChoice']=='' and returned['view']['Revision']==run.revision)
    run.ui(0,'action',value='primary')
    target=next(u['Position'] for u in before['Units'] if u['Id']=='hero:1')
    selected=run.ui(0,'cell',cell=target)
    run.check('map check enabled before commit',selected['worldConfirm'] and selected['confirmEnabled'])
    run.ui(0,'disconnect')
    frozen=run.ui(0,'view')
    run.check('disconnect disables world decisions and clears preview',not frozen['worldDecisionsEnabled'] and not frozen['confirmEnabled'] and frozen['actionChoice']=='')
    run.ui(0,'connect');run.ui(0,'action',value='primary');run.ui(0,'cell',cell=target)
    committed=run.ui(0,'confirm')
    run.check('UI atomic commit accepted',committed['result']['Accepted'])
    run.revision=committed['view']['Revision']
    run.check('one commit enters actual defender response',run.revision==before['Revision']+1 and run.view(1)['Pending']['Kind']=='defense')
    run.report['passed']=True
except Exception:
    run.report['passed']=False;run.report['failure']=traceback.format_exc();raise
finally:run.finish()

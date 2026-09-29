"""Actual TCP authority: private preview, atomic target commit and reconnect receipt."""
import traceback
from acceptance import Run

run = Run('tests/scenarios/combat-defense.json', 7)
try:
    run.connect_all()
    view = run.view(0)
    run.revision = view['Revision']
    run.check('attacker receives legal primary preview', 'hero:1' in view['PrimaryPreview']['Targets'])
    run.check('opponent receives no private preview', run.view(1).get('PrimaryPreview') is None)
    revision = run.revision
    run.command(0, 'CommitPrimaryAttack', expected='invalid_attack_target', Value='missing')
    run.check('invalid target rolls back entire start', run.view(0)['Revision'] == revision and run.view(0)['Phase'] == 'Action')
    result = run.command(0, 'CommitPrimaryAttack', Value='hero:1')
    run.check('single revision enters defense', run.revision == revision + 1 and run.view(1)['Pending']['Kind'] == 'defense')
    run.check('attacker cannot undo once confirmed', run.view(0).get('PrimaryPreview') is None)
    run.rpc(0, 'disconnect')
    run.rpc(0, 'connect')
    run.check('reconnect sees committed defender response', run.view(0)['Pending']['Kind'] == 'defense')
    run.report['passed'] = True
except Exception:
    run.report['passed'] = False
    run.report['failure'] = traceback.format_exc()
    raise
finally:
    run.finish()

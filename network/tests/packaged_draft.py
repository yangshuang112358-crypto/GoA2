"""Transport fixture for both legacy and engine98 packages, not a physics test."""
def complete_packaged_draft(players, check):
    revision = max(p.view['Revision'] for p in players)

    def command(seat, kind, value):
        nonlocal revision
        players[seat].wait(lambda: players[seat].view['Revision'] >= revision)
        result = players[seat].submit(kind, {'Value': value})
        check(f'packaged {kind} seat {seat}', result.get('Accepted') is True)
        revision = result['Snapshot']['Revision']

    opening = players[0].view.get('Opening')
    order = (0, 1, 2, 3)
    if opening and opening['Purpose'] == 'draft':
        toss = opening['TossId']
        # Authenticated host's deterministic fixture, not claimed as a Unity toss.
        players[0].send({'Type': 'CoinMotion', 'Frame': {
            'TossId': toss, 'Sequence': 1, 'Time': 1,
            'Position': [0, .12, 0], 'Rotation': [0, 0, 0, 1],
            'Velocity': [0, 0, 0], 'AngularVelocity': [0, 0, 0]}})
        command(0, 'ReportCoinToss', toss+'|Red|0,0,0,1')
        order = (1, 0, 2, 3)
    heroes = ('wasp', 'sabina', 'tigerclaw', 'arien')
    for seat in order:
        command(seat, 'ChooseHero', heroes[seat])
    return revision

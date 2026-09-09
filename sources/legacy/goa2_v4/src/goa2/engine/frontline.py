"""Pure round-end frontline and victory queries."""

from __future__ import annotations

from goa2.domain import BoardCell, MinionKind, MinionState, Team, TeamState


def spawn_minions_for_region(
    board: tuple[BoardCell, ...],
    region: str,
) -> tuple[MinionState, ...]:
    result = []
    for team in Team:
        for kind in MinionKind:
            spawn_state = f"{team.value}{kind.value.title()}Spawn"
            cells = sorted(
                (
                    cell
                    for cell in board
                    if cell.region == region
                    and cell.state == spawn_state
                    and not cell.obstacle
                ),
                key=lambda cell: (cell.y, cell.x),
            )
            for index, cell in enumerate(cells):
                result.append(
                    MinionState(
                        unit_id=f"{team.value}-{kind.value}-{index}",
                        team=team,
                        kind=kind,
                        x=cell.x,
                        y=cell.y,
                    )
                )
    return tuple(result)


def minion_count_difference(
    minions: tuple[MinionState, ...],
) -> tuple[Team, int] | None:
    counts = {
        team: sum(minion.team is team for minion in minions)
        for team in Team
    }
    if counts[Team.BLUE] == counts[Team.RED]:
        return None
    losing_team = (
        Team.BLUE
        if counts[Team.BLUE] < counts[Team.RED]
        else Team.RED
    )
    return losing_team, abs(counts[Team.BLUE] - counts[Team.RED])


def legal_minion_removal_ids(
    minions: tuple[MinionState, ...],
    team: Team,
) -> tuple[str, ...]:
    team_minions = tuple(minion for minion in minions if minion.team is team)
    non_heavy = tuple(
        minion for minion in team_minions if minion.kind is not MinionKind.HEAVY
    )
    candidates = non_heavy or team_minions
    return tuple(sorted(minion.unit_id for minion in candidates))


def winner_from_team_state(
    teams: tuple[TeamState, TeamState],
) -> Team | None:
    crystal_losers = [team.team for team in teams if team.crystal_life <= 0]
    mark_winners = [team.team for team in teams if team.frontline_marks >= 3]
    outcomes = [
        *(Team.RED if team is Team.BLUE else Team.BLUE for team in crystal_losers),
        *mark_winners,
    ]
    unique = set(outcomes)
    if len(unique) > 1:
        raise ValueError("conflicting victory outcomes are not allowed")
    return next(iter(unique), None)

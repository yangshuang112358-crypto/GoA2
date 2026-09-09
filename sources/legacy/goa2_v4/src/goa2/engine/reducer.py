"""Pure command reducer for the room shell."""

from __future__ import annotations

from collections import deque
from dataclasses import dataclass, replace

from goa2.domain import (
    ActionSkipped,
    BasicAttackDeclared,
    BasicDefenseResolved,
    BeginRoundEnd,
    CaptainChoiceResolved,
    CardSelected,
    CardsRevealed,
    CancelResolutionAction,
    ChooseResolutionAction,
    ChooseCardUpgrade,
    CompletePendingAction,
    Command,
    ConfirmSelection,
    ControlledSeatSwitched,
    CrystalLifeDebugSet,
    DebugAdvanceFrontline,
    DebugDefeatHero,
    Event,
    ForceConfirmSelections,
    DebugTeleportHero,
    DebugEnterRoundEnd,
    DebugRemoveMinion,
    DebugResetMinions,
    DebugSetCrystalLife,
    DebugSetFrontlineMarks,
    DebugSetSeatCoins,
    DeclareBasicAttack,
    FrontlineDebugAdvanced,
    FrontlineMarksDebugSet,
    HeroDebugDefeated,
    HeroDebugTeleported,
    HeroFastMoved,
    HeroMoved,
    HeroRespawned,
    InitiativeChoiceResolved,
    MoveHero,
    MinionKind,
    MinionState,
    MinionDebugRemoved,
    MinionsDebugReset,
    MinionDefeated,
    PendingActionCompleted,
    PendingAttack,
    PendingCaptainChoice,
    PendingInitiativeChoice,
    PendingRespawn,
    PendingUpgrade,
    Phase,
    RevealCards,
    ResolutionActionChosen,
    ResolutionActionCancelled,
    ResolveCaptainChoice,
    ResolveInitiativeChoice,
    ResolveBasicDefense,
    ResolveRespawn,
    ResetRoom,
    RoundEndStarted,
    RoundEndStep,
    RoomSeats,
    RoomReset,
    RoomState,
    Seat,
    SeatCoinsDebugSet,
    SeatState,
    SelectCard,
    SelectionConfirmed,
    SelectionsForceConfirmed,
    SkipAction,
    SwitchControlledSeat,
    Team,
    create_initial_room_state,
)
from .modifiers import modified_value


class IllegalCommand(ValueError):
    """Raised when a command cannot be applied to the supplied state."""


@dataclass(frozen=True, slots=True)
class Reduction:
    state: RoomState
    events: tuple[Event, ...]


def reduce_room(state: RoomState, command: Command) -> Reduction:
    if command.expected_revision != state.revision:
        raise IllegalCommand(
            f"revision mismatch: expected {command.expected_revision}, "
            f"current {state.revision}"
        )

    if isinstance(command, SwitchControlledSeat):
        if command.target_seat == state.controlled_seat:
            raise IllegalCommand("target seat is already controlled")

        next_revision = state.revision + 1
        next_state = replace(
            state,
            revision=next_revision,
            controlled_seat=command.target_seat,
        )
        event = ControlledSeatSwitched(
            revision=next_revision,
            previous_seat=state.controlled_seat,
            controlled_seat=command.target_seat,
        )
        return Reduction(next_state, (event,))

    if isinstance(command, SelectCard):
        _require_phase(state, Phase.CARD_SELECTION)
        _require_controlled_seat(state, command.seat)
        current_seat = state.seats[int(command.seat)]
        if current_seat.confirmed:
            raise IllegalCommand("confirmed seats cannot change selection")
        if current_seat.selected_card_id == command.card_id:
            raise IllegalCommand("card is already selected")

        next_revision = state.revision + 1
        next_seat = replace(current_seat, selected_card_id=command.card_id)
        next_state = replace(
            state,
            revision=next_revision,
            seats=_replace_seat(state, next_seat),
        )
        event = CardSelected(
            revision=next_revision,
            seat=command.seat,
            card_id=command.card_id,
        )
        return Reduction(next_state, (event,))

    if isinstance(command, ConfirmSelection):
        _require_phase(state, Phase.CARD_SELECTION)
        _require_controlled_seat(state, command.seat)
        current_seat = state.seats[int(command.seat)]
        if current_seat.confirmed:
            raise IllegalCommand("seat selection is already confirmed")
        if current_seat.selected_card_id is None:
            raise IllegalCommand("a card must be selected before confirmation")

        next_revision = state.revision + 1
        next_seat = replace(current_seat, confirmed=True)
        next_seats = _replace_seat(state, next_seat)
        next_phase = (
            Phase.CARD_REVEAL
            if all(seat.confirmed for seat in next_seats)
            else state.phase
        )
        next_state = replace(
            state,
            revision=next_revision,
            phase=next_phase,
            seats=next_seats,
        )
        event = SelectionConfirmed(
            revision=next_revision,
            seat=command.seat,
            phase=next_phase,
        )
        return Reduction(next_state, (event,))

    if isinstance(command, ForceConfirmSelections):
        _require_phase(state, Phase.CARD_SELECTION)
        next_seats = tuple(
            seat_state
            if seat_state.confirmed
            else replace(
                seat_state,
                selected_card_id=(
                    seat_state.selected_card_id
                    or command.default_cards[int(seat_state.seat)]
                ),
                confirmed=True,
            )
            for seat_state in state.seats
        )
        next_revision = state.revision + 1
        next_state = replace(
            state,
            revision=next_revision,
            phase=Phase.CARD_REVEAL,
            seats=next_seats,
        )
        event = SelectionsForceConfirmed(
            revision=next_revision,
            phase=Phase.CARD_REVEAL,
        )
        return Reduction(next_state, (event,))

    if isinstance(command, RevealCards):
        if state.phase is not Phase.CARD_REVEAL:
            raise IllegalCommand("cards can only be revealed after selection")
        supplied_seats = tuple(card[0] for card in command.cards)
        if set(supplied_seats) != set(Seat):
            raise IllegalCommand("reveal data must contain each seat exactly once")

        cards_by_seat = {seat: card_id for seat, card_id, _ in command.cards}
        profiles_by_seat = dict(command.profiles)
        for seat_state in state.seats:
            if seat_state.selected_card_id is None:
                raise IllegalCommand("all seats must select a card before reveal")
            if not seat_state.confirmed:
                raise IllegalCommand("all seats must confirm before reveal")
            if cards_by_seat[seat_state.seat] != seat_state.selected_card_id:
                raise IllegalCommand("revealed cards must match selected cards")

        ordered_cards = tuple(
            sorted(command.cards, key=lambda card: (-card[2], int(card[0])))
        )
        initiative_order = tuple(card[0] for card in ordered_cards)
        next_revision = state.revision + 1
        next_seats = tuple(
            replace(
                seat_state,
                confirmed=True,
                revealed_card_id=cards_by_seat[seat_state.seat],
                action_profile=profiles_by_seat[seat_state.seat],
            )
            for seat_state in state.seats
        )
        next_state = _prepare_next_resolution(replace(
            state,
            revision=next_revision,
            phase=Phase.CARD_RESOLUTION,
            seats=next_seats,
            initiative_order=initiative_order,
            revealed_initiatives=tuple(
                next(card[2] for card in command.cards if card[0] == Seat(seat))
                for seat in range(4)
            ),
            resolved_seats=(),
            active_seat=initiative_order[0],
            pending_initiative_choice=None,
        ))
        event = CardsRevealed(
            revision=next_revision,
            cards=tuple(
                next(card for card in ordered_cards if card[0] == seat)
                for seat in next_state.initiative_order
            ),
            initiative_order=next_state.initiative_order,
            active_seat=next_state.active_seat,
            phase=Phase.CARD_RESOLUTION,
            profiles=command.profiles,
        )
        return Reduction(next_state, (event,))

    if isinstance(command, ResolveInitiativeChoice):
        _require_phase(state, Phase.CARD_RESOLUTION)
        _require_controlled_seat(state, command.seat)
        choice = state.pending_initiative_choice
        if choice is None:
            raise IllegalCommand("no initiative choice is pending")
        if command.seat != choice.chooser:
            raise IllegalCommand("only the team captain may choose initiative")
        if command.choice_id != choice.choice_id:
            raise IllegalCommand("initiative choice id does not match")
        if command.chosen_seat not in choice.candidate_seats:
            raise IllegalCommand("chosen seat is not an initiative candidate")
        prefix_length = len(state.resolved_seats)
        remaining = list(state.initiative_order[prefix_length:])
        remaining.remove(command.chosen_seat)
        order = (
            *state.initiative_order[:prefix_length],
            command.chosen_seat,
            *remaining,
        )
        next_revision = state.revision + 1
        chosen_state = _prepare_respawn(replace(
                state,
                revision=next_revision,
                initiative_order=order,
                active_seat=command.chosen_seat,
                pending_initiative_choice=None,
                pending_respawn=None,
            ))
        return Reduction(
            chosen_state,
            (
                InitiativeChoiceResolved(
                    revision=next_revision,
                    captain_seat=command.seat,
                    chosen_seat=command.chosen_seat,
                    initiative=choice.initiative,
                ),
            ),
        )

    if isinstance(command, ResolveRespawn):
        _require_phase(state, Phase.CARD_RESOLUTION)
        _require_controlled_seat(state, command.seat)
        pending = state.pending_respawn
        if pending is None or pending.seat != command.seat:
            raise IllegalCommand("this seat has no pending respawn")
        if (command.x, command.y) not in pending.candidate_cells:
            raise IllegalCommand("respawn cell is not legal")
        units = list(state.units)
        units[int(command.seat)] = replace(
            units[int(command.seat)],
            x=command.x,
            y=command.y,
        )
        seats = list(state.seats)
        seats[int(command.seat)] = replace(
            seats[int(command.seat)],
            defeated=False,
        )
        next_revision = state.revision + 1
        return Reduction(
            replace(
                state,
                revision=next_revision,
                units=tuple(units),
                seats=tuple(seats),
                pending_respawn=None,
            ),
            (
                HeroRespawned(
                    revision=next_revision,
                    seat=command.seat,
                    x=command.x,
                    y=command.y,
                ),
            ),
        )

    if isinstance(command, ChooseCardUpgrade):
        _require_phase(state, Phase.UPGRADE)
        _require_controlled_seat(state, command.seat)
        seat_state = state.seats[int(command.seat)]
        if seat_state.pending_upgrade_choices <= 0:
            raise IllegalCommand("this seat has no pending upgrade")
        color_index = {"red": 0, "green": 1, "blue": 2}[command.color]
        next_level = seat_state.skill_levels[color_index] + 1
        if next_level > 3:
            raise IllegalCommand("this color is already fully upgraded")
        if next_level == 3 and min(seat_state.skill_levels) < 2:
            raise IllegalCommand("all colors must reach level two first")
        active_cards = list(seat_state.active_card_ids)
        if command.current_card_id not in active_cards:
            raise IllegalCommand("active card for upgrade color is missing")
        old_index = active_cards.index(command.current_card_id)
        active_cards[old_index] = command.chosen_card_id
        levels = list(seat_state.skill_levels)
        levels[color_index] = next_level
        bonuses = list(seat_state.passive_bonuses)
        bonuses[command.passive_index] += 1
        remaining = seat_state.pending_upgrade_choices - 1
        seats = list(state.seats)
        seats[int(command.seat)] = replace(
            seat_state,
            skill_levels=tuple(levels),
            active_card_ids=tuple(active_cards),
            passive_bonuses=tuple(bonuses),
            pending_upgrade_choices=remaining,
        )
        next_state = replace(
            state,
            revision=state.revision + 1,
            seats=tuple(seats),
            pending_upgrade=None,
        )
        if not any(seat.pending_upgrade_choices > 0 for seat in next_state.seats):
            next_state = _advance_upgrade_queue(next_state)
        return Reduction(next_state, ())

    if isinstance(command, ChooseResolutionAction):
        _require_phase(state, Phase.CARD_RESOLUTION)
        _require_controlled_seat(state, command.seat)
        if command.seat != state.active_seat:
            raise IllegalCommand("only the active seat may choose an action")
        if state.pending_respawn is not None:
            raise IllegalCommand("the active hero must respawn before choosing an action")
        if state.pending_attack is not None:
            raise IllegalCommand("an attack target is already locked")
        profile = state.seats[int(command.seat)].action_profile
        if profile is None or not _choice_allowed(profile, command.choice):
            raise IllegalCommand("action choice is not available for the revealed card")
        if (
            command.choice.kind == "primary"
            and profile.primary_family == "attack"
            and not basic_attack_targets(state, command.seat)
        ):
            raise IllegalCommand("basic attack has no adjacent unit target")
        next_revision = state.revision + 1
        next_state = replace(
            state,
            revision=next_revision,
            pending_action=command.choice,
            pending_movement=_movement_budget(
                profile,
                command.choice,
                state.seats[int(command.seat)],
            ),
        )
        return Reduction(
            next_state,
            (
                ResolutionActionChosen(
                    revision=next_revision,
                    seat=command.seat,
                    choice=command.choice,
                ),
            ),
        )

    if isinstance(command, CancelResolutionAction):
        _require_phase(state, Phase.CARD_RESOLUTION)
        _require_controlled_seat(state, command.seat)
        if command.seat != state.active_seat:
            raise IllegalCommand("only the active seat may cancel its action")
        if state.pending_action is None:
            raise IllegalCommand("no action is pending")
        if state.pending_attack is not None:
            raise IllegalCommand("an attack target is already locked")
        next_revision = state.revision + 1
        return Reduction(
            replace(
                state,
                revision=next_revision,
                pending_action=None,
                pending_movement=None,
                pending_attack=None,
            ),
            (
                ResolutionActionCancelled(
                    revision=next_revision,
                    seat=command.seat,
                ),
            ),
        )

    if isinstance(command, DeclareBasicAttack):
        _require_phase(state, Phase.CARD_RESOLUTION)
        _require_controlled_seat(state, command.seat)
        if command.seat != state.active_seat:
            raise IllegalCommand("only the active seat may declare an attack")
        if state.pending_attack is not None:
            raise IllegalCommand("an attack is already pending")
        if state.pending_action is None or state.pending_action.kind != "primary":
            raise IllegalCommand("basic attack requires a pending primary action")
        seat_state = state.seats[int(command.seat)]
        profile = seat_state.action_profile
        if profile is None or profile.primary_family != "attack":
            raise IllegalCommand("revealed card is not an attack")
        targets = {target_id: kind for target_id, kind in basic_attack_targets(
            state, command.seat
        )}
        target_kind = targets.get(command.target_id)
        if target_kind is None:
            raise IllegalCommand("target is not an adjacent unit")
        if seat_state.revealed_card_id is None:
            raise IllegalCommand("active seat has no revealed card")
        next_revision = state.revision + 1
        base_attack = modified_value(
            seat_state,
            "attack",
            profile.primary_value,
        )
        support_bonus, defense_reduction = attack_minion_modifiers(
            state, command.target_id
        )
        pending = PendingAttack(
            attacker_seat=command.seat,
            card_id=seat_state.revealed_card_id,
            target_id=command.target_id,
            target_kind=target_kind,
            base_attack=base_attack,
            support_bonus=support_bonus,
            defense_reduction=defense_reduction,
            attack_value=base_attack + support_bonus - defense_reduction,
        )
        if target_kind == "minion":
            defeated_minion = next(
                minion for minion in state.minions
                if minion.unit_id == command.target_id
            )
            reward = 4 if defeated_minion.kind is MinionKind.HEAVY else 2
            rewarded_seats = list(state.seats)
            rewarded_seats[int(command.seat)] = replace(
                rewarded_seats[int(command.seat)],
                coins=rewarded_seats[int(command.seat)].coins + reward,
            )
            defeated_state = replace(
                state,
                revision=next_revision,
                seats=tuple(rewarded_seats),
                minions=tuple(
                    minion
                    for minion in state.minions
                    if minion.unit_id != command.target_id
                ),
                pending_action=None,
                pending_movement=None,
            )
            if defeated_minion.kind is MinionKind.HEAVY:
                next_state = _advance_frontline(
                    replace(defeated_state, resume_action_after_frontline=True),
                    defeated_minion.team,
                )
            else:
                next_state = _complete_active_action(defeated_state)
        else:
            defender = next(
                unit.seat
                for unit in state.units
                if unit.unit_id == command.target_id
            )
            next_state = replace(
                state,
                revision=next_revision,
                controlled_seat=defender,
                pending_attack=pending,
            )
        events: tuple[Event, ...] = (
            BasicAttackDeclared(
                revision=next_revision,
                seat=command.seat,
                card_id=pending.card_id,
                target_id=pending.target_id,
                target_kind=pending.target_kind,
                base_attack=pending.base_attack,
                support_bonus=pending.support_bonus,
                defense_reduction=pending.defense_reduction,
                attack_value=pending.attack_value,
            ),
        )
        if target_kind == "minion":
            events = (
                *events,
                MinionDefeated(
                    revision=next_revision,
                    attacker_seat=command.seat,
                    minion_id=command.target_id,
                    attack_value=pending.attack_value,
                ),
            )
        return Reduction(
            next_state,
            events,
        )

    if isinstance(command, ResolveBasicDefense):
        _require_phase(state, Phase.CARD_RESOLUTION)
        _require_controlled_seat(state, command.seat)
        pending = state.pending_attack
        if pending is None or pending.target_kind != "hero":
            raise IllegalCommand("no hero defense is pending")
        defender_unit = next(
            (unit for unit in state.units if unit.unit_id == pending.target_id),
            None,
        )
        if defender_unit is None or defender_unit.seat != command.seat:
            raise IllegalCommand("only the attacked hero may resolve defense")
        defender = state.seats[int(command.seat)]
        if defender.defeated:
            raise IllegalCommand("defender is already defeated")
        if command.card_id is not None:
            unavailable = {
                *defender.used_card_ids,
                *defender.discarded_card_ids,
                defender.revealed_card_id,
            }
            if command.card_id in unavailable:
                raise IllegalCommand("defense card is not available")
        effective_defense = (
            modified_value(defender, "defense", command.defense_value)
            if command.card_id is not None and command.defense_value is not None
            else None
        )
        defended = command.card_id is not None and (
            command.exclamation
            or effective_defense >= pending.attack_value
        )
        defeated = not defended
        next_revision = state.revision + 1
        seats = list(state.seats)
        seats[int(command.seat)] = replace(
            defender,
            discarded_card_ids=(
                (*defender.discarded_card_ids, command.card_id)
                if command.card_id is not None
                else defender.discarded_card_ids
            ),
        )
        next_state = replace(
            state,
            revision=next_revision,
            seats=tuple(seats),
            pending_attack=None,
            pending_action=None,
            pending_movement=None,
        )
        if defeated:
            next_state = _defeat_hero(
                next_state,
                command.seat,
                rewarding_attacker=pending.attacker_seat,
            )
        if next_state.phase is not Phase.GAME_OVER:
            next_state = _complete_active_action(next_state)
        return Reduction(
            next_state,
            (
                BasicDefenseResolved(
                    revision=next_revision,
                    attacker_seat=pending.attacker_seat,
                    defender_seat=command.seat,
                    card_id=command.card_id,
                    defense_value=effective_defense,
                    attack_value=pending.attack_value,
                    defended=defended,
                    defeated=defeated,
                ),
            ),
        )

    if isinstance(command, MoveHero):
        _require_phase(state, Phase.CARD_RESOLUTION)
        _require_controlled_seat(state, command.seat)
        if command.seat != state.active_seat:
            raise IllegalCommand("only the active seat may move")
        if state.pending_action is not None and state.pending_action.kind == "fast_move":
            targets = fast_move_targets(state, command.seat)
            destination = (command.x, command.y)
            if destination not in targets:
                raise IllegalCommand("destination is not a legal fast move target")
            unit = state.units[int(command.seat)]
            cells = {(cell.x, cell.y): cell for cell in state.board}
            moved = replace(unit, x=command.x, y=command.y)
            units = list(state.units)
            units[int(command.seat)] = moved
            next_revision = state.revision + 1
            next_state = _complete_active_action(
                replace(
                    state,
                    revision=next_revision,
                    units=tuple(units),
                    pending_action=None,
                    pending_movement=None,
                )
            )
            return Reduction(
                next_state,
                (
                    HeroFastMoved(
                        revision=next_revision,
                        seat=command.seat,
                        source=(unit.x, unit.y),
                        destination=destination,
                        source_region=cells[(unit.x, unit.y)].region,
                        destination_region=cells[destination].region,
                    ),
                ),
            )
        if state.pending_movement is None or state.pending_movement <= 0:
            raise IllegalCommand("no ordinary movement is pending")
        costs, paths = reachable_cells(state, command.seat, state.pending_movement)
        destination = (command.x, command.y)
        if destination not in costs:
            raise IllegalCommand("destination is not reachable")
        unit = state.units[int(command.seat)]
        moved = replace(unit, x=command.x, y=command.y)
        units = list(state.units)
        units[int(command.seat)] = moved
        next_revision = state.revision + 1
        next_state = _complete_active_action(
            replace(
                state,
                revision=next_revision,
                units=tuple(units),
                pending_action=None,
                pending_movement=None,
            )
        )
        return Reduction(
            next_state,
            (
                HeroMoved(
                    revision=next_revision,
                    seat=command.seat,
                    source=(unit.x, unit.y),
                    destination=destination,
                    path=paths[destination],
                    cost=costs[destination],
                ),
            ),
        )

    if isinstance(command, CompletePendingAction):
        _require_phase(state, Phase.CARD_RESOLUTION)
        _require_controlled_seat(state, command.seat)
        if command.seat != state.active_seat:
            raise IllegalCommand("only the active seat may complete its action")
        choice = state.pending_action
        profile = state.seats[int(command.seat)].action_profile
        if choice is None or choice.kind != "primary":
            raise IllegalCommand("only a pending primary action can be completed")
        if state.pending_movement is not None:
            raise IllegalCommand("movement actions require a legal destination")
        if profile is None or profile.primary_is_movement:
            raise IllegalCommand("movement actions require a legal destination")
        if profile.primary_category == "基础攻击":
            raise IllegalCommand("basic attacks require a legal target")
        if profile.implementation_status != "data_only":
            raise IllegalCommand("implemented actions cannot use data-only completion")
        card_id = state.seats[int(command.seat)].revealed_card_id
        if card_id is None:
            raise IllegalCommand("active seat has no revealed card")
        next_revision = state.revision + 1
        next_state = _complete_active_action(
            replace(
                state,
                revision=next_revision,
                pending_action=None,
                pending_movement=None,
                pending_attack=None,
            )
        )
        return Reduction(
            next_state,
            (
                PendingActionCompleted(
                    revision=next_revision,
                    seat=command.seat,
                    card_id=card_id,
                    primary_family=profile.primary_family,
                    next_active_seat=next_state.active_seat,
                    phase=next_state.phase.value,
                ),
            ),
        )

    if isinstance(command, DebugSetSeatCoins):
        _require_debug_game_active(state)
        next_revision = state.revision + 1
        seats = list(state.seats)
        seats[int(command.seat)] = replace(
            seats[int(command.seat)],
            coins=command.coins,
        )
        return Reduction(
            replace(state, revision=next_revision, seats=tuple(seats)),
            (
                SeatCoinsDebugSet(
                    revision=next_revision,
                    seat=command.seat,
                    coins=command.coins,
                ),
            ),
        )

    if isinstance(command, DebugSetCrystalLife):
        _require_debug_game_active(state)
        next_revision = state.revision + 1
        teams = list(state.teams)
        team_index = 0 if command.team is Team.BLUE else 1
        teams[team_index] = replace(
            teams[team_index],
            crystal_life=command.crystal_life,
        )
        next_state = replace(state, revision=next_revision, teams=tuple(teams))
        if command.crystal_life == 0:
            next_state = _finish_game(
                next_state,
                Team.RED if command.team is Team.BLUE else Team.BLUE,
            )
        return Reduction(
            next_state,
            (
                CrystalLifeDebugSet(
                    revision=next_revision,
                    team=command.team,
                    crystal_life=command.crystal_life,
                    winner=next_state.winner,
                ),
            ),
        )

    if isinstance(command, DebugSetFrontlineMarks):
        _require_debug_game_active(state)
        next_revision = state.revision + 1
        teams = list(state.teams)
        team_index = 0 if command.team is Team.BLUE else 1
        teams[team_index] = replace(
            teams[team_index],
            frontline_marks=command.frontline_marks,
        )
        next_state = replace(state, revision=next_revision, teams=tuple(teams))
        if command.frontline_marks >= state.frontline_victory_marks:
            next_state = _finish_game(next_state, command.team)
        return Reduction(
            next_state,
            (
                FrontlineMarksDebugSet(
                    revision=next_revision,
                    team=command.team,
                    frontline_marks=command.frontline_marks,
                    winner=next_state.winner,
                ),
            ),
        )

    if isinstance(command, DebugDefeatHero):
        _require_debug_game_active(state)
        defender = state.seats[int(command.seat)]
        if defender.defeated:
            raise IllegalCommand("hero is already defeated")
        if (
            state.phase is Phase.CARD_RESOLUTION
            and state.active_seat == command.seat
            and (
                state.pending_action is not None
                or state.pending_attack is not None
                or state.pending_movement is not None
            )
        ):
            raise IllegalCommand("cannot debug defeat an active hero mid-action")
        if (
            state.pending_attack is not None
            and state.pending_attack.target_id == state.units[int(command.seat)].unit_id
        ):
            raise IllegalCommand("cannot debug defeat a hero with pending defense")
        next_revision = state.revision + 1
        next_state = _defeat_hero(
            replace(state, revision=next_revision),
            command.seat,
        )
        if (
            next_state.phase is Phase.CARD_RESOLUTION
            and next_state.active_seat == command.seat
        ):
            next_state = _prepare_respawn(next_state)
        return Reduction(
            next_state,
            (
                HeroDebugDefeated(
                    revision=next_revision,
                    seat=command.seat,
                    crystal_damage=defender.hero_level,
                    winner=next_state.winner,
                ),
            ),
        )

    if isinstance(command, DebugResetMinions):
        _require_debug_game_active(state)
        from goa2.engine.frontline import spawn_minions_for_region

        region = state.combat_region or "mid"
        minions = spawn_minions_for_region(state.board, region)
        next_revision = state.revision + 1
        return Reduction(
            replace(
                state,
                revision=next_revision,
                minions=minions,
                pending_captain_choice=None,
            ),
            (
                MinionsDebugReset(
                    revision=next_revision,
                    combat_region=region,
                    minion_count=len(minions),
                ),
            ),
        )

    if isinstance(command, DebugAdvanceFrontline):
        _require_debug_game_active(state)
        previous_region = state.combat_region or "mid"
        next_revision = state.revision + 1
        losing_team = Team.RED if command.team is Team.BLUE else Team.BLUE
        next_state = _advance_frontline(
            replace(state, revision=next_revision),
            losing_team,
        )
        team_state = next(
            team for team in next_state.teams if team.team is command.team
        )
        return Reduction(
            next_state,
            (
                FrontlineDebugAdvanced(
                    revision=next_revision,
                    team=command.team,
                    previous_region=previous_region,
                    combat_region=next_state.combat_region,
                    frontline_marks=team_state.frontline_marks,
                    winner=next_state.winner,
                ),
            ),
        )

    if isinstance(command, DebugTeleportHero):
        _require_controlled_seat(state, command.seat)
        cell = next(
            (
                cell
                for cell in state.board
                if cell.x == command.x and cell.y == command.y
            ),
            None,
        )
        if cell is None or cell.obstacle:
            raise IllegalCommand("debug teleport destination is not walkable")
        occupied = {
            (unit.x, unit.y)
            for unit in state.units
            if unit.seat != command.seat
        } | {(minion.x, minion.y) for minion in state.minions}
        if (command.x, command.y) in occupied:
            raise IllegalCommand("debug teleport destination is occupied")
        unit = state.units[int(command.seat)]
        if (command.x, command.y) == (unit.x, unit.y):
            raise IllegalCommand("hero is already at the destination")
        units = list(state.units)
        units[int(command.seat)] = replace(unit, x=command.x, y=command.y)
        next_revision = state.revision + 1
        return Reduction(
            replace(state, revision=next_revision, units=tuple(units)),
            (
                HeroDebugTeleported(
                    revision=next_revision,
                    seat=command.seat,
                    source=(unit.x, unit.y),
                    destination=(command.x, command.y),
                ),
            ),
        )

    if isinstance(command, DebugEnterRoundEnd):
        if state.phase is Phase.GAME_OVER:
            raise IllegalCommand("cannot enter round end after game over")
        next_revision = state.revision + 1
        return Reduction(
            replace(
                state,
                revision=next_revision,
                phase=Phase.ROUND_END,
                initiative_order=(),
                resolved_seats=(),
                active_seat=None,
                pending_action=None,
                pending_movement=None,
                pending_attack=None,
                pending_initiative_choice=None,
                round_end_step=RoundEndStep.READY,
                pending_captain_choice=None,
            ),
            (),
        )

    if isinstance(command, DebugRemoveMinion):
        _require_controlled_seat(state, command.seat)
        if state.phase is Phase.GAME_OVER:
            raise IllegalCommand("cannot remove a minion after game over")
        removed = next(
            (minion for minion in state.minions if minion.unit_id == command.minion_id),
            None,
        )
        if removed is None:
            raise IllegalCommand("unknown minion id")
        next_revision = state.revision + 1
        next_state = replace(
            state,
            revision=next_revision,
            minions=tuple(
                minion
                for minion in state.minions
                if minion.unit_id != command.minion_id
            ),
        )
        if removed.kind is MinionKind.HEAVY:
            next_state = _advance_frontline(next_state, removed.team)
        return Reduction(
            next_state,
            (
                MinionDebugRemoved(
                    revision=next_revision,
                    seat=command.seat,
                    minion_id=command.minion_id,
                ),
            ),
        )

    if isinstance(command, BeginRoundEnd):
        _require_phase(state, Phase.ROUND_END)
        if state.round_end_step is not RoundEndStep.READY:
            raise IllegalCommand("round end has already started")
        next_revision = state.revision + 1
        next_state = _begin_frontline_resolution(replace(
            state,
            revision=next_revision,
            round_end_step=RoundEndStep.FRONTLINE,
        ))
        return Reduction(
            next_state,
            (
                RoundEndStarted(
                    revision=next_revision,
                    step=RoundEndStep.FRONTLINE.value,
                ),
            ),
        )

    if isinstance(command, ResolveCaptainChoice):
        _require_controlled_seat(state, command.seat)
        choice = state.pending_captain_choice
        if choice is None:
            raise IllegalCommand("no captain choice is pending")
        if command.seat != choice.chooser:
            raise IllegalCommand("only the assigned captain may resolve this choice")
        if command.choice_id != choice.choice_id:
            raise IllegalCommand("captain choice id does not match")
        if command.candidate_id not in choice.candidate_ids:
            raise IllegalCommand("candidate is not legal for this captain choice")
        next_revision = state.revision + 1
        if choice.kind == "remove_minions":
            minions = tuple(
                minion
                for minion in state.minions
                if minion.unit_id != command.candidate_id
            )
            remaining = choice.required_count - 1
            next_state = replace(
                state,
                revision=next_revision,
                minions=minions,
                pending_captain_choice=None,
            )
            removed = next(
                minion for minion in state.minions
                if minion.unit_id == command.candidate_id
            )
            if removed.kind is MinionKind.HEAVY:
                next_state = _advance_frontline(next_state, choice.team)
            elif remaining > 0:
                from goa2.engine.frontline import legal_minion_removal_ids

                next_state = replace(
                    next_state,
                    pending_captain_choice=replace(
                        choice,
                        candidate_ids=legal_minion_removal_ids(minions, choice.team),
                        required_count=remaining,
                    ),
                )
            else:
                next_state = _begin_upgrade_phase(next_state)
        elif choice.kind == "choose_spawn_cell":
            if choice.subject_id is None or choice.subject_kind is None:
                raise IllegalCommand("spawn choice is missing its minion subject")
            x_text, y_text = command.candidate_id.split(",", 1)
            minion = _minion_for_spawn_choice(
                choice, int(x_text), int(y_text)
            )
            next_state = _continue_frontline_spawn(
                replace(
                    state,
                    revision=next_revision,
                    minions=(*state.minions, minion),
                    pending_captain_choice=None,
                )
            )
        else:
            raise IllegalCommand("unsupported captain choice kind")
        return Reduction(
            next_state,
            (
                CaptainChoiceResolved(
                    revision=next_revision,
                    seat=command.seat,
                    choice_kind=choice.kind,
                    candidate_id=command.candidate_id,
                ),
            ),
        )

    if isinstance(command, SkipAction):
        _require_phase(state, Phase.CARD_RESOLUTION)
        _require_controlled_seat(state, command.seat)
        if command.seat != state.active_seat:
            raise IllegalCommand("only the active seat may skip its action")
        if state.pending_respawn is not None:
            raise IllegalCommand("the active hero must respawn before skipping")
        if state.pending_action is not None:
            raise IllegalCommand("cannot skip while an action is pending")

        next_revision = state.revision + 1
        next_state = _complete_active_action(replace(state, revision=next_revision))
        event = ActionSkipped(
            revision=next_revision,
            seat=command.seat,
            next_active_seat=next_state.active_seat,
            phase=next_state.phase,
        )
        return Reduction(next_state, (event,))

    if isinstance(command, ResetRoom):
        from goa2.engine.frontline import spawn_minions_for_region

        next_revision = state.revision + 1
        initial = create_initial_room_state(
            starting_crystal_life=state.starting_crystal_life,
            frontline_victory_marks=state.frontline_victory_marks,
        )
        reset_units = tuple(
            replace(unit, x=unit.spawn_x, y=unit.spawn_y) for unit in state.units
        )
        next_state = replace(
            initial,
            revision=next_revision,
            board=state.board,
            units=reset_units,
            minions=spawn_minions_for_region(state.board, "mid"),
        )
        event = RoomReset(
            revision=next_revision,
            phase=next_state.phase,
            controlled_seat=next_state.controlled_seat,
        )
        return Reduction(next_state, (event,))

    raise IllegalCommand(f"unsupported command: {type(command).__name__}")


def _require_phase(state: RoomState, phase: Phase) -> None:
    if state.phase is not phase:
        raise IllegalCommand(
            f"command requires phase {phase.value}, current phase is {state.phase.value}"
        )


def _require_debug_game_active(state: RoomState) -> None:
    if state.phase is Phase.GAME_OVER:
        raise IllegalCommand("debug command is not available after game over")


def _require_controlled_seat(state: RoomState, seat: Seat) -> None:
    # Multiplayer authorization is enforced by the room identity adapter.
    # controlled_seat remains a hotseat/debug display hint only.
    return None


def _replace_seat(state: RoomState, next_seat: SeatState) -> RoomSeats:
    seats = list(state.seats)
    seats[int(next_seat.seat)] = next_seat
    return tuple(seats)  # type: ignore[return-value]


def _choice_allowed(profile: object, choice: object) -> bool:
    from goa2.domain import ActionChoice, ActionProfile

    if not isinstance(profile, ActionProfile) or not isinstance(choice, ActionChoice):
        return False
    if choice.kind == "primary":
        return True
    if choice.kind == "secondary_movement":
        return profile.secondary_movement
    return choice.source_slot in profile.fast_move_sources


def _movement_budget(
    profile: object,
    choice: object,
    seat_state: SeatState | None = None,
) -> int | None:
    from goa2.domain import ActionChoice, ActionProfile

    if not isinstance(profile, ActionProfile) or not isinstance(choice, ActionChoice):
        return None
    if choice.kind == "primary" and profile.primary_is_movement:
        return (
            modified_value(seat_state, "movement", profile.primary_value)
            if seat_state is not None
            else profile.primary_value
        )
    if choice.kind == "secondary_movement":
        return (
            modified_value(
                seat_state,
                "movement",
                profile.secondary_movement_value,
            )
            if seat_state is not None
            else profile.secondary_movement_value
        )
    return None


def _prepare_next_resolution(state: RoomState) -> RoomState:
    unresolved = state.initiative_order[len(state.resolved_seats) :]
    if not unresolved:
        return state
    top = max(state.revealed_initiatives[int(seat)] for seat in unresolved)
    tied = tuple(
        seat
        for seat in unresolved
        if state.revealed_initiatives[int(seat)] == top
    )
    tied_teams = {state.seats[int(seat)].team for seat in tied}
    decision_coin = state.decision_coin
    chosen_team = next(iter(tied_teams))
    if len(tied_teams) > 1:
        chosen_team = decision_coin
        decision_coin = Team.RED if decision_coin is Team.BLUE else Team.BLUE
    candidates = tuple(
        seat for seat in tied if state.seats[int(seat)].team is chosen_team
    )
    if len(candidates) > 1:
        team_state = next(team for team in state.teams if team.team is chosen_team)
        if team_state.captain_seat is None:
            raise IllegalCommand("tied team has no captain")
        return replace(
            state,
            controlled_seat=team_state.captain_seat,
            active_seat=None,
            decision_coin=decision_coin,
            pending_initiative_choice=PendingInitiativeChoice(
                choice_id=(
                    f"initiative:{state.round_number}:{state.turn_number}:"
                    f"{top}:{chosen_team.value}:{len(state.resolved_seats)}"
                ),
                team=chosen_team,
                chooser=team_state.captain_seat,
                candidate_seats=candidates,
                initiative=top,
            ),
        )
    chosen = candidates[0]
    prefix_length = len(state.resolved_seats)
    remaining = list(unresolved)
    remaining.remove(chosen)
    return _prepare_respawn(replace(
        state,
        initiative_order=(
            *state.initiative_order[:prefix_length],
            chosen,
            *remaining,
        ),
        active_seat=chosen,
        decision_coin=decision_coin,
        pending_initiative_choice=None,
        pending_respawn=None,
    ))


def _prepare_respawn(state: RoomState) -> RoomState:
    active = state.active_seat
    if active is None or not state.seats[int(active)].defeated:
        return state
    team = state.seats[int(active)].team
    spawn_state = "blueHeroSpawn" if team is Team.BLUE else "redHeroSpawn"
    occupied = {
        (unit.x, unit.y)
        for unit in state.units
        if unit.seat != active and not state.seats[int(unit.seat)].defeated
    } | {
        (minion.x, minion.y) for minion in state.minions
    }
    candidates = tuple(
        sorted(
            (cell.x, cell.y)
            for cell in state.board
            if cell.state == spawn_state
            and not cell.obstacle
            and (cell.x, cell.y) not in occupied
        )
    )
    if not candidates:
        raise IllegalCommand("defeated hero has no legal respawn cell")
    return replace(
        state,
        controlled_seat=active,
        pending_respawn=PendingRespawn(active, candidates),
    )


def _complete_active_action(state: RoomState) -> RoomState:
    if state.active_seat is None:
        raise IllegalCommand("no active seat")
    active = state.active_seat
    active_state = state.seats[int(active)]
    if active_state.revealed_card_id is None:
        raise IllegalCommand("active seat has no revealed card")
    seats = list(state.seats)
    seats[int(active)] = replace(
        active_state,
        used_card_ids=(*active_state.used_card_ids, active_state.revealed_card_id),
    )
    resolved = (*state.resolved_seats, active)
    unresolved = state.initiative_order[len(resolved) :]
    if unresolved:
        return _prepare_next_resolution(replace(
            state,
            seats=tuple(seats),
            resolved_seats=resolved,
            active_seat=unresolved[0],
            pending_initiative_choice=None,
        ))
    next_turn = (
        state.turn_number
        if state.turn_number == 4
        else state.turn_number + 1
    )
    next_phase = Phase.ROUND_END if state.turn_number == 4 else Phase.CARD_SELECTION
    reset_seats = tuple(
        replace(
            seat,
            selected_card_id=None,
            confirmed=False,
            revealed_card_id=None,
            action_profile=None,
        )
        for seat in seats
    )
    next_state = replace(
        state,
        phase=next_phase,
        seats=reset_seats,
        initiative_order=(),
        revealed_initiatives=(),
        resolved_seats=(),
        active_seat=None,
        pending_action=None,
        pending_movement=None,
        pending_attack=None,
        pending_initiative_choice=None,
        pending_respawn=None,
        turn_number=next_turn,
    )
    if next_phase is Phase.ROUND_END:
        return _begin_frontline_resolution(replace(
            next_state,
            round_end_step=RoundEndStep.FRONTLINE,
        ))
    return next_state


def _begin_frontline_resolution(state: RoomState) -> RoomState:
    from goa2.engine.frontline import (
        legal_minion_removal_ids,
        minion_count_difference,
    )

    difference = minion_count_difference(state.minions)
    if difference is None:
        return _begin_upgrade_phase(state)
    losing_team, count = difference
    team_state = next(team for team in state.teams if team.team is losing_team)
    if team_state.captain_seat is None:
        raise IllegalCommand("losing team has no captain")
    return replace(
        state,
        pending_captain_choice=PendingCaptainChoice(
            choice_id=f"remove:{state.round_number}:{losing_team.value}",
            kind="remove_minions",
            team=losing_team,
            chooser=team_state.captain_seat,
            candidate_ids=legal_minion_removal_ids(state.minions, losing_team),
            required_count=count,
            source_step=RoundEndStep.FRONTLINE,
        ),
    )


def _advance_frontline(state: RoomState, losing_team: Team) -> RoomState:
    advancing_team = Team.RED if losing_team is Team.BLUE else Team.BLUE
    teams = list(state.teams)
    team_index = 0 if advancing_team is Team.BLUE else 1
    teams[team_index] = replace(
        teams[team_index],
        frontline_marks=teams[team_index].frontline_marks + 1,
    )
    regions = ("redNear", "mid", "blueNear")
    current_index = regions.index(state.combat_region or "mid")
    next_index = current_index - 1 if advancing_team is Team.BLUE else current_index + 1
    boundary_victory = next_index < 0 or next_index >= len(regions)
    mark_victory = (
        teams[team_index].frontline_marks >= state.frontline_victory_marks
    )
    if boundary_victory or mark_victory:
        return _finish_game(
            replace(state, teams=tuple(teams), minions=()),
            advancing_team,
        )
    return _continue_frontline_spawn(
        replace(
            state,
            teams=tuple(teams),
            minions=(),
            combat_region=regions[next_index],
            pending_captain_choice=None,
        )
    )


def _defeat_hero(
    state: RoomState,
    defender_seat: Seat,
    *,
    rewarding_attacker: Seat | None = None,
) -> RoomState:
    defender = state.seats[int(defender_seat)]
    if defender.defeated:
        raise IllegalCommand("hero is already defeated")

    seats = list(state.seats)
    seats[int(defender_seat)] = replace(defender, defeated=True)
    if rewarding_attacker is not None:
        defeated_level = defender.hero_level
        assist_gold = (
            1 if defeated_level <= 3 else 2 if defeated_level <= 6 else 3
        )
        attacker_team = seats[int(rewarding_attacker)].team
        seats[int(rewarding_attacker)] = replace(
            seats[int(rewarding_attacker)],
            coins=seats[int(rewarding_attacker)].coins + defeated_level,
        )
        for index, teammate in enumerate(seats):
            if (
                teammate.team is attacker_team
                and teammate.seat != rewarding_attacker
            ):
                seats[index] = replace(
                    teammate,
                    coins=teammate.coins + assist_gold,
                )

    units = list(state.units)
    units[int(defender_seat)] = replace(
        units[int(defender_seat)],
        x=10000 + int(defender_seat),
        y=10000,
    )
    teams = list(state.teams)
    team_index = 0 if defender.team is Team.BLUE else 1
    teams[team_index] = replace(
        teams[team_index],
        crystal_life=teams[team_index].crystal_life - defender.hero_level,
    )
    next_state = replace(
        state,
        seats=tuple(seats),
        units=tuple(units),
        teams=tuple(teams),
    )
    if teams[team_index].crystal_life <= 0:
        return _finish_game(
            next_state,
            Team.RED if defender.team is Team.BLUE else Team.BLUE,
        )
    return next_state


def _finish_game(state: RoomState, winner: Team) -> RoomState:
    return replace(
        state,
        phase=Phase.GAME_OVER,
        initiative_order=(),
        revealed_initiatives=(),
        resolved_seats=(),
        active_seat=None,
        pending_action=None,
        pending_movement=None,
        pending_attack=None,
        pending_initiative_choice=None,
        pending_respawn=None,
        pending_captain_choice=None,
        pending_upgrade=None,
        round_end_step=RoundEndStep.COMPLETE,
        resume_action_after_frontline=False,
        winner=winner,
    )


def _continue_frontline_spawn(state: RoomState) -> RoomState:
    from goa2.engine.frontline import spawn_minions_for_region

    expected = spawn_minions_for_region(state.board, state.combat_region or "mid")
    current_ids = {minion.unit_id for minion in state.minions}
    occupied = {
        (unit.x, unit.y) for unit in state.units
    } | {
        (minion.x, minion.y) for minion in state.minions
    }
    board = {
        (cell.x, cell.y): cell for cell in state.board if not cell.obstacle
    }
    minions = list(state.minions)
    directions = ((1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1))
    for minion in expected:
        if minion.unit_id in current_ids:
            continue
        coordinate = (minion.x, minion.y)
        if coordinate not in occupied:
            minions.append(minion)
            occupied.add(coordinate)
            continue
        candidates = tuple(
            f"{coordinate[0] + dx},{coordinate[1] + dy}"
            for dx, dy in directions
            if (coordinate[0] + dx, coordinate[1] + dy) in board
            and (coordinate[0] + dx, coordinate[1] + dy) not in occupied
        )
        if not candidates:
            raise IllegalCommand("occupied minion spawn has no adjacent legal cell")
        team_state = next(team for team in state.teams if team.team is minion.team)
        if team_state.captain_seat is None:
            raise IllegalCommand("spawning team has no captain")
        return replace(
            state,
            minions=tuple(minions),
            pending_captain_choice=PendingCaptainChoice(
                choice_id=f"spawn:{state.combat_region}:{minion.unit_id}",
                kind="choose_spawn_cell",
                team=minion.team,
                chooser=team_state.captain_seat,
                candidate_ids=candidates,
                required_count=1,
                source_step=RoundEndStep.FRONTLINE,
                subject_id=minion.unit_id,
                subject_kind=minion.kind,
            ),
        )
    completed = replace(state, minions=tuple(minions))
    if completed.resume_action_after_frontline:
        return _complete_active_action(
            replace(completed, resume_action_after_frontline=False)
        )
    if completed.phase is Phase.ROUND_END:
        return _begin_upgrade_phase(completed)
    return completed


def _minion_for_spawn_choice(
    choice: PendingCaptainChoice,
    x: int,
    y: int,
) -> MinionState:
    if choice.subject_id is None or choice.subject_kind is None:
        raise IllegalCommand("spawn choice is missing its minion subject")
    return MinionState(
        unit_id=choice.subject_id,
        team=choice.team,
        kind=choice.subject_kind,
        x=x,
        y=y,
    )


def _begin_upgrade_phase(state: RoomState) -> RoomState:
    seats = []
    for seat in state.seats:
        coins = seat.coins
        level = seat.hero_level
        starting_level = level
        choices = 0
        while level < 8 and coins >= level:
            coins -= level
            level += 1
            if level < 8:
                choices += 1
        if level == starting_level:
            coins += 1
        seats.append(
            replace(
                seat,
                coins=coins,
                hero_level=level,
                pending_upgrade_choices=choices,
            )
        )
    prepared = replace(state, seats=tuple(seats))
    return _advance_upgrade_queue(prepared)


def _advance_upgrade_queue(state: RoomState) -> RoomState:
    if not any(seat.pending_upgrade_choices > 0 for seat in state.seats):
        return _start_next_round(replace(state, pending_upgrade=None))
    return replace(
        state,
        phase=Phase.UPGRADE,
        pending_upgrade=None,
        initiative_order=(),
        revealed_initiatives=(),
        resolved_seats=(),
        active_seat=None,
        pending_action=None,
        pending_movement=None,
        pending_attack=None,
        pending_initiative_choice=None,
        pending_captain_choice=None,
        round_end_step=RoundEndStep.COMPLETE,
    )


def _start_next_round(state: RoomState) -> RoomState:
    seats = tuple(
        replace(
            seat,
            selected_card_id=None,
            confirmed=False,
            revealed_card_id=None,
            action_profile=None,
            used_card_ids=(),
            discarded_card_ids=(),
            pending_upgrade_choices=0,
        )
        for seat in state.seats
    )
    return replace(
        state,
        phase=Phase.CARD_SELECTION,
        seats=seats,
        initiative_order=(),
        revealed_initiatives=(),
        resolved_seats=(),
        active_seat=None,
        pending_action=None,
        pending_movement=None,
        pending_attack=None,
        pending_initiative_choice=None,
        round_number=state.round_number + 1,
        turn_number=1,
        round_end_step=RoundEndStep.READY,
        pending_captain_choice=None,
        pending_upgrade=None,
        resume_action_after_frontline=False,
    )


def reachable_cells(
    state: RoomState, seat: Seat, budget: int
) -> tuple[dict[tuple[int, int], int], dict[tuple[int, int], tuple[tuple[int, int], ...]]]:
    if state.seats[int(seat)].defeated:
        return {}, {}
    unit = state.units[int(seat)]
    start = (unit.x, unit.y)
    cells = {(cell.x, cell.y): cell for cell in state.board if not cell.obstacle}
    occupied = {
        (other.x, other.y) for other in state.units if other.seat != seat
    } | {
        (minion.x, minion.y) for minion in state.minions
    }
    costs: dict[tuple[int, int], int] = {}
    paths: dict[tuple[int, int], tuple[tuple[int, int], ...]] = {}
    queue = deque([(start, (start,))])
    seen = {start}
    directions = ((1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1))
    while queue:
        current, path = queue.popleft()
        cost = len(path) - 1
        if cost >= budget:
            continue
        for dx, dy in directions:
            target = (current[0] + dx, current[1] + dy)
            if target in seen or target not in cells or target in occupied:
                continue
            seen.add(target)
            next_path = (*path, target)
            costs[target] = cost + 1
            paths[target] = next_path
            queue.append((target, next_path))
    return costs, paths


def basic_attack_targets(
    state: RoomState,
    seat: Seat,
) -> tuple[tuple[str, str], ...]:
    if state.seats[int(seat)].defeated:
        return ()
    attacker = state.units[int(seat)]
    adjacent = {
        (attacker.x + dx, attacker.y + dy)
        for dx, dy in ((1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1))
    }
    targets = [
        (unit.unit_id, "hero")
        for unit in state.units
        if unit.team is not attacker.team
        and not state.seats[int(unit.seat)].defeated
        and (unit.x, unit.y) in adjacent
    ]
    targets.extend(
        (minion.unit_id, "minion")
        for minion in state.minions
        if minion.team is not attacker.team
        and (minion.x, minion.y) in adjacent
        and not (
            minion.kind is MinionKind.HEAVY
            and any(
                ally.team is minion.team
                and ally.kind is not MinionKind.HEAVY
                for ally in state.minions
            )
        )
    )
    return tuple(sorted(targets))


def attack_minion_modifiers(
    state: RoomState,
    target_id: str,
) -> tuple[int, int]:
    defender = next(
        (unit for unit in state.units if unit.unit_id == target_id),
        None,
    )
    if defender is None:
        return (0, 0)
    support_bonus = sum(
        1
        for minion in state.minions
        if minion.team is not defender.team
        and (
            (
                minion.kind in {MinionKind.MELEE, MinionKind.HEAVY}
                and _hex_distance(
                    (minion.x, minion.y), (defender.x, defender.y)
                ) == 1
            )
            or (
                minion.kind is MinionKind.RANGED
                and _hex_distance(
                    (minion.x, minion.y), (defender.x, defender.y)
                ) <= 2
            )
        )
    )
    defense_reduction = sum(
        1
        for minion in state.minions
        if minion.team is defender.team
        and minion.kind is MinionKind.MELEE
        and _hex_distance((minion.x, minion.y), (defender.x, defender.y)) == 1
    )
    return support_bonus, defense_reduction


def _hex_distance(source: tuple[int, int], target: tuple[int, int]) -> int:
    dx = source[0] - target[0]
    dy = source[1] - target[1]
    return max(abs(dx), abs(dy), abs(dx + dy))


def adjacent_regions(state: RoomState) -> dict[str, tuple[str, ...]]:
    cells = {(cell.x, cell.y): cell for cell in state.board if not cell.obstacle}
    graph: dict[str, set[str]] = {}
    directions = ((1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1))
    for coordinate, cell in cells.items():
        graph.setdefault(cell.region, set())
        for dx, dy in directions:
            neighbor = cells.get((coordinate[0] + dx, coordinate[1] + dy))
            if neighbor is not None and neighbor.region != cell.region:
                graph[cell.region].add(neighbor.region)
    return {region: tuple(sorted(neighbors)) for region, neighbors in graph.items()}


def fast_move_targets(
    state: RoomState,
    seat: Seat,
) -> tuple[tuple[int, int], ...]:
    unit = state.units[int(seat)]
    cells = {
        (cell.x, cell.y): cell for cell in state.board if not cell.obstacle
    }
    source_cell = cells.get((unit.x, unit.y))
    if source_cell is None:
        return ()
    enemy_regions = {
        cells[(other.x, other.y)].region
        for other in (*state.units, *state.minions)
        if other.team is not unit.team and (other.x, other.y) in cells
    }
    if source_cell.region in enemy_regions:
        return ()
    allowed_regions = {
        source_cell.region,
        *adjacent_regions(state).get(source_cell.region, ()),
    } - enemy_regions
    occupied = {
        (other.x, other.y) for other in state.units
    } | {
        (minion.x, minion.y) for minion in state.minions
    }
    return tuple(
        sorted(
            (
                coordinate
                for coordinate, cell in cells.items()
                if cell.region in allowed_regions and coordinate not in occupied
            ),
            key=lambda coordinate: (coordinate[1], coordinate[0]),
        )
    )

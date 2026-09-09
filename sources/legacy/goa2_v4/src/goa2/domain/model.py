"""Immutable room state for the four-seat hotseat shell."""

from __future__ import annotations

from dataclasses import dataclass
from enum import IntEnum, StrEnum
from secrets import choice
from typing import TypeAlias


JsonScalar: TypeAlias = str | int | float | bool | None
JsonValue: TypeAlias = JsonScalar | list["JsonValue"] | dict[str, "JsonValue"]


class Seat(IntEnum):
    ZERO = 0
    ONE = 1
    TWO = 2
    THREE = 3


class Team(StrEnum):
    BLUE = "blue"
    RED = "red"


class MinionKind(StrEnum):
    MELEE = "melee"
    RANGED = "ranged"
    HEAVY = "heavy"


class RoundEndStep(StrEnum):
    READY = "ready"
    FRONTLINE = "frontline"
    COMPLETE = "complete"


@dataclass(frozen=True, slots=True)
class ActionProfile:
    implementation_status: str
    primary_family: str
    primary_category: str
    primary_is_movement: bool
    secondary_movement: bool
    fast_move_sources: tuple[str, ...]
    primary_value: int
    secondary_movement_value: int | None

    def __post_init__(self) -> None:
        if not self.implementation_status:
            raise ValueError("implementation_status must be non-empty")
        if not self.primary_family:
            raise ValueError("primary_family must be non-empty")
        if not self.primary_category:
            raise ValueError("primary_category must be non-empty")
        if self.primary_is_movement and self.secondary_movement:
            raise ValueError("primary movement cards cannot have secondary movement")
        if any(source not in {"primary", "secondary"} for source in self.fast_move_sources):
            raise ValueError("invalid fast move source")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "implementation_status": self.implementation_status,
            "primary_family": self.primary_family,
            "primary_category": self.primary_category,
            "primary_is_movement": self.primary_is_movement,
            "secondary_movement": self.secondary_movement,
            "fast_move_sources": list(self.fast_move_sources),
            "primary_value": self.primary_value,
            "secondary_movement_value": self.secondary_movement_value,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "ActionProfile":
        if set(value) != {
            "implementation_status",
            "primary_family",
            "primary_category",
            "primary_is_movement",
            "secondary_movement",
            "fast_move_sources",
            "primary_value",
            "secondary_movement_value",
        }:
            raise ValueError("action profile has unexpected fields")
        sources = value["fast_move_sources"]
        if not isinstance(sources, list):
            raise ValueError("fast_move_sources must be a list")
        return cls(
            implementation_status=value["implementation_status"],
            primary_family=value["primary_family"],
            primary_category=value["primary_category"],
            primary_is_movement=value["primary_is_movement"],
            secondary_movement=value["secondary_movement"],
            fast_move_sources=tuple(sources),
            primary_value=value["primary_value"],
            secondary_movement_value=value["secondary_movement_value"],
        )


@dataclass(frozen=True, slots=True)
class BoardCell:
    x: int
    y: int
    obstacle: bool
    region: str
    state: str

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "x": self.x,
            "y": self.y,
            "obstacle": self.obstacle,
            "region": self.region,
            "state": self.state,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "BoardCell":
        return cls(
            x=value["x"],
            y=value["y"],
            obstacle=value["obstacle"],
            region=value["region"],
            state=value["state"],
        )


@dataclass(frozen=True, slots=True)
class UnitState:
    unit_id: str
    seat: Seat
    team: Team
    x: int
    y: int
    spawn_x: int
    spawn_y: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "unit_id": self.unit_id, "seat": int(self.seat), "team": self.team.value,
            "x": self.x, "y": self.y, "spawn_x": self.spawn_x, "spawn_y": self.spawn_y,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "UnitState":
        return cls(
            unit_id=value["unit_id"], seat=Seat(value["seat"]), team=Team(value["team"]),
            x=value["x"], y=value["y"], spawn_x=value["spawn_x"], spawn_y=value["spawn_y"],
        )


@dataclass(frozen=True, slots=True)
class MinionState:
    unit_id: str
    team: Team
    kind: MinionKind
    x: int
    y: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "unit_id": self.unit_id,
            "team": self.team.value,
            "kind": self.kind.value,
            "x": self.x,
            "y": self.y,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "MinionState":
        if set(value) != {"unit_id", "team", "kind", "x", "y"}:
            raise ValueError("minion state has unexpected fields")
        return cls(
            unit_id=value["unit_id"],
            team=Team(value["team"]),
            kind=MinionKind(value["kind"]),
            x=value["x"],
            y=value["y"],
        )


@dataclass(frozen=True, slots=True)
class TeamState:
    team: Team
    crystal_life: int = 7
    frontline_marks: int = 0
    captain_seat: Seat | None = None

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "team": self.team.value,
            "crystal_life": self.crystal_life,
            "frontline_marks": self.frontline_marks,
            "captain_seat": (
                int(self.captain_seat) if self.captain_seat is not None else None
            ),
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "TeamState":
        if set(value) != {
            "team",
            "crystal_life",
            "frontline_marks",
            "captain_seat",
        }:
            raise ValueError("team state has unexpected fields")
        captain = value["captain_seat"]
        return cls(
            team=Team(value["team"]),
            crystal_life=value["crystal_life"],
            frontline_marks=value["frontline_marks"],
            captain_seat=Seat(captain) if captain is not None else None,
        )


@dataclass(frozen=True, slots=True)
class PendingCaptainChoice:
    choice_id: str
    kind: str
    team: Team
    chooser: Seat
    candidate_ids: tuple[str, ...]
    required_count: int
    source_step: RoundEndStep
    subject_id: str | None = None
    subject_kind: MinionKind | None = None

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "choice_id": self.choice_id,
            "kind": self.kind,
            "team": self.team.value,
            "chooser": int(self.chooser),
            "candidate_ids": list(self.candidate_ids),
            "required_count": self.required_count,
            "source_step": self.source_step.value,
            "subject_id": self.subject_id,
            "subject_kind": (
                self.subject_kind.value if self.subject_kind is not None else None
            ),
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "PendingCaptainChoice":
        if set(value) != {
            "choice_id",
            "kind",
            "team",
            "chooser",
            "candidate_ids",
            "required_count",
            "source_step",
            "subject_id",
            "subject_kind",
        }:
            raise ValueError("pending captain choice has unexpected fields")
        return cls(
            choice_id=value["choice_id"],
            kind=value["kind"],
            team=Team(value["team"]),
            chooser=Seat(value["chooser"]),
            candidate_ids=tuple(value["candidate_ids"]),
            required_count=value["required_count"],
            source_step=RoundEndStep(value["source_step"]),
            subject_id=value["subject_id"],
            subject_kind=(
                MinionKind(value["subject_kind"])
                if value["subject_kind"] is not None
                else None
            ),
        )


@dataclass(frozen=True, slots=True)
class PendingInitiativeChoice:
    choice_id: str
    team: Team
    chooser: Seat
    candidate_seats: tuple[Seat, ...]
    initiative: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "choice_id": self.choice_id,
            "team": self.team.value,
            "chooser": int(self.chooser),
            "candidate_seats": [int(seat) for seat in self.candidate_seats],
            "initiative": self.initiative,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "PendingInitiativeChoice":
        if set(value) != {
            "choice_id",
            "team",
            "chooser",
            "candidate_seats",
            "initiative",
        }:
            raise ValueError("pending initiative choice has unexpected fields")
        return cls(
            choice_id=value["choice_id"],
            team=Team(value["team"]),
            chooser=Seat(value["chooser"]),
            candidate_seats=tuple(Seat(seat) for seat in value["candidate_seats"]),
            initiative=value["initiative"],
        )


@dataclass(frozen=True, slots=True)
class PendingUpgrade:
    seat: Seat
    remaining_choices: int

    def __post_init__(self) -> None:
        if self.remaining_choices <= 0:
            raise ValueError("pending upgrade choices must be positive")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "seat": int(self.seat),
            "remaining_choices": self.remaining_choices,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "PendingUpgrade":
        if set(value) != {"seat", "remaining_choices"}:
            raise ValueError("pending upgrade has unexpected fields")
        return cls(
            seat=Seat(value["seat"]),
            remaining_choices=value["remaining_choices"],
        )


@dataclass(frozen=True, slots=True)
class PendingRespawn:
    seat: Seat
    candidate_cells: tuple[tuple[int, int], ...]

    def __post_init__(self) -> None:
        if not self.candidate_cells:
            raise ValueError("pending respawn requires candidate cells")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "seat": int(self.seat),
            "candidate_cells": [
                {"x": x, "y": y} for x, y in self.candidate_cells
            ],
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "PendingRespawn":
        if set(value) != {"seat", "candidate_cells"}:
            raise ValueError("pending respawn has unexpected fields")
        return cls(
            seat=Seat(value["seat"]),
            candidate_cells=tuple(
                (cell["x"], cell["y"]) for cell in value["candidate_cells"]
            ),
        )


@dataclass(frozen=True, slots=True)
class ActionChoice:
    kind: str
    source_slot: str | None = None

    def __post_init__(self) -> None:
        if self.kind not in {"primary", "secondary_movement", "fast_move"}:
            raise ValueError("invalid action choice kind")
        if self.kind == "fast_move":
            if self.source_slot not in {"primary", "secondary"}:
                raise ValueError("fast move requires a source slot")
        elif self.source_slot is not None:
            raise ValueError("source_slot is only valid for fast move")

    def to_dict(self) -> dict[str, JsonValue]:
        return {"kind": self.kind, "source_slot": self.source_slot}

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "ActionChoice":
        if set(value) != {"kind", "source_slot"}:
            raise ValueError("action choice has unexpected fields")
        return cls(kind=value["kind"], source_slot=value["source_slot"])


@dataclass(frozen=True, slots=True)
class PendingAttack:
    attacker_seat: Seat
    card_id: str
    target_id: str
    target_kind: str
    base_attack: int
    support_bonus: int
    defense_reduction: int
    attack_value: int

    def __post_init__(self) -> None:
        if self.target_kind not in {"hero", "minion"}:
            raise ValueError("pending attack target kind must be hero or minion")
        if not self.card_id or not self.target_id:
            raise ValueError("pending attack requires card and target ids")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "attacker_seat": int(self.attacker_seat),
            "card_id": self.card_id,
            "target_id": self.target_id,
            "target_kind": self.target_kind,
            "base_attack": self.base_attack,
            "support_bonus": self.support_bonus,
            "defense_reduction": self.defense_reduction,
            "attack_value": self.attack_value,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "PendingAttack":
        if set(value) != {
            "attacker_seat",
            "card_id",
            "target_id",
            "target_kind",
            "base_attack",
            "support_bonus",
            "defense_reduction",
            "attack_value",
        }:
            raise ValueError("pending attack has unexpected fields")
        return cls(
            attacker_seat=Seat(value["attacker_seat"]),
            card_id=value["card_id"],
            target_id=value["target_id"],
            target_kind=value["target_kind"],
            base_attack=value["base_attack"],
            support_bonus=value["support_bonus"],
            defense_reduction=value["defense_reduction"],
            attack_value=value["attack_value"],
        )


class Phase(StrEnum):
    SETUP = "setup"
    CARD_SELECTION = "card_selection"
    CARD_REVEAL = "card_reveal"
    CARD_RESOLUTION = "card_resolution"
    ROUND_END = "round_end"
    UPGRADE = "upgrade"
    GAME_OVER = "game_over"


@dataclass(frozen=True, slots=True)
class SeatState:
    seat: Seat
    team: Team
    selected_card_id: str | None = None
    confirmed: bool = False
    revealed_card_id: str | None = None
    action_profile: ActionProfile | None = None
    used_card_ids: tuple[str, ...] = ()
    discarded_card_ids: tuple[str, ...] = ()
    defeated: bool = False
    coins: int = 0
    hero_level: int = 1
    skill_levels: tuple[int, int, int] = (1, 1, 1)
    active_card_ids: tuple[str, ...] = ()
    passive_bonuses: tuple[int, int, int, int, int, int] = (0, 0, 0, 0, 0, 0)
    pending_upgrade_choices: int = 0

    def __post_init__(self) -> None:
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")
        if not isinstance(self.team, Team):
            raise TypeError("team must be a Team")
        if self.selected_card_id is not None and (
            not isinstance(self.selected_card_id, str) or not self.selected_card_id
        ):
            raise ValueError("selected_card_id must be a non-empty string or None")
        if not isinstance(self.confirmed, bool):
            raise TypeError("confirmed must be a boolean")
        if self.confirmed and self.selected_card_id is None:
            raise ValueError("confirmed seats must have a selected card")
        if self.revealed_card_id is not None and (
            not isinstance(self.revealed_card_id, str) or not self.revealed_card_id
        ):
            raise ValueError("revealed_card_id must be a non-empty string or None")
        if (
            self.revealed_card_id is not None
            and self.revealed_card_id != self.selected_card_id
        ):
            raise ValueError("revealed card must match the selected card")
        if self.action_profile is not None and self.revealed_card_id is None:
            raise ValueError("action profile requires a revealed card")
        if self.coins < 0:
            raise ValueError("coins cannot be negative")
        if not 1 <= self.hero_level <= 8:
            raise ValueError("hero level must be from 1 to 8")
        if len(self.skill_levels) != 3 or any(
            level not in {1, 2, 3} for level in self.skill_levels
        ):
            raise ValueError("skill levels must contain red, green, blue levels")
        if len(self.passive_bonuses) != 6 or any(
            bonus < 0 for bonus in self.passive_bonuses
        ):
            raise ValueError("passive bonuses must contain six non-negative values")
        if self.pending_upgrade_choices < 0:
            raise ValueError("pending upgrade choices cannot be negative")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "seat": int(self.seat),
            "team": self.team.value,
            "selected_card_id": self.selected_card_id,
            "confirmed": self.confirmed,
            "revealed_card_id": self.revealed_card_id,
            "action_profile": (
                self.action_profile.to_dict() if self.action_profile is not None else None
            ),
            "used_card_ids": list(self.used_card_ids),
            "discarded_card_ids": list(self.discarded_card_ids),
            "defeated": self.defeated,
            "coins": self.coins,
            "hero_level": self.hero_level,
            "skill_levels": list(self.skill_levels),
            "active_card_ids": list(self.active_card_ids),
            "passive_bonuses": list(self.passive_bonuses),
            "pending_upgrade_choices": self.pending_upgrade_choices,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "SeatState":
        expected_fields = {
            "seat",
            "team",
            "selected_card_id",
            "confirmed",
            "revealed_card_id",
            "action_profile",
            "used_card_ids",
            "discarded_card_ids",
            "defeated",
            "coins",
            "hero_level",
            "skill_levels",
            "active_card_ids",
            "passive_bonuses",
            "pending_upgrade_choices",
        }
        if set(value) != expected_fields:
            raise ValueError("seat state has unexpected fields")
        return cls(
            seat=Seat(value["seat"]),
            team=Team(value["team"]),
            selected_card_id=value["selected_card_id"],
            confirmed=value["confirmed"],
            revealed_card_id=value["revealed_card_id"],
            action_profile=(
                ActionProfile.from_dict(value["action_profile"])
                if value["action_profile"] is not None
                else None
            ),
            used_card_ids=tuple(value["used_card_ids"]),
            discarded_card_ids=tuple(value["discarded_card_ids"]),
            defeated=value["defeated"],
            coins=value["coins"],
            hero_level=value["hero_level"],
            skill_levels=tuple(value["skill_levels"]),
            active_card_ids=tuple(value["active_card_ids"]),
            passive_bonuses=tuple(value["passive_bonuses"]),
            pending_upgrade_choices=value["pending_upgrade_choices"],
        )


RoomSeats: TypeAlias = tuple[SeatState, SeatState, SeatState, SeatState]
RoomHeroIds: TypeAlias = tuple[str, str, str, str]
DEFAULT_HERO_IDS: RoomHeroIds = ("wasp", "shargatha", "brogan", "arien")


@dataclass(frozen=True, slots=True)
class RoomState:
    revision: int
    phase: Phase
    controlled_seat: Seat
    seats: RoomSeats
    hero_ids: RoomHeroIds = DEFAULT_HERO_IDS
    initiative_order: tuple[Seat, ...] = ()
    revealed_initiatives: tuple[int, ...] = ()
    resolved_seats: tuple[Seat, ...] = ()
    active_seat: Seat | None = None
    pending_action: ActionChoice | None = None
    pending_movement: int | None = None
    pending_attack: PendingAttack | None = None
    pending_initiative_choice: PendingInitiativeChoice | None = None
    pending_respawn: PendingRespawn | None = None
    board: tuple[BoardCell, ...] = ()
    units: tuple[UnitState, ...] = ()
    starting_crystal_life: int = 7
    frontline_victory_marks: int = 3
    decision_coin: Team = Team.BLUE
    round_number: int = 1
    turn_number: int = 1
    minions: tuple[MinionState, ...] = ()
    teams: tuple[TeamState, TeamState] = (
        TeamState(Team.BLUE, captain_seat=Seat.ZERO),
        TeamState(Team.RED, captain_seat=Seat.ONE),
    )
    combat_region: str | None = None
    round_end_step: RoundEndStep = RoundEndStep.READY
    pending_captain_choice: PendingCaptainChoice | None = None
    pending_upgrade: PendingUpgrade | None = None
    resume_action_after_frontline: bool = False
    winner: Team | None = None

    def __post_init__(self) -> None:
        if not isinstance(self.revision, int) or isinstance(self.revision, bool):
            raise TypeError("revision must be an integer")
        if self.revision < 0:
            raise ValueError("revision must be non-negative")
        if not isinstance(self.phase, Phase):
            raise TypeError("phase must be a Phase")
        if not isinstance(self.controlled_seat, Seat):
            raise TypeError("controlled_seat must be a Seat")

        expected_layout = (
            (Seat.ZERO, Team.BLUE),
            (Seat.ONE, Team.RED),
            (Seat.TWO, Team.BLUE),
            (Seat.THREE, Team.RED),
        )
        actual_layout = tuple((seat.seat, seat.team) for seat in self.seats)
        if actual_layout != expected_layout:
            raise ValueError("room seats must use the fixed four-seat team layout")
        if (
            not isinstance(self.hero_ids, tuple)
            or len(self.hero_ids) != 4
            or any(not isinstance(hero_id, str) or not hero_id for hero_id in self.hero_ids)
        ):
            raise ValueError("hero_ids must contain four non-empty strings")
        if len(set(self.hero_ids)) != 4:
            raise ValueError("hero_ids must contain four distinct heroes")
        if not isinstance(self.initiative_order, tuple) or any(
            not isinstance(seat, Seat) for seat in self.initiative_order
        ):
            raise TypeError("initiative_order must be a tuple of seats")
        if len(set(self.initiative_order)) != len(self.initiative_order):
            raise ValueError("initiative_order cannot contain duplicate seats")
        if self.revealed_initiatives and len(self.revealed_initiatives) != 4:
            raise ValueError("revealed initiatives must contain four seat values")
        if not isinstance(self.resolved_seats, tuple) or any(
            not isinstance(seat, Seat) for seat in self.resolved_seats
        ):
            raise TypeError("resolved_seats must be a tuple of seats")
        if len(set(self.resolved_seats)) != len(self.resolved_seats):
            raise ValueError("resolved_seats cannot contain duplicate seats")
        if self.resolved_seats != self.initiative_order[: len(self.resolved_seats)]:
            raise ValueError("resolved_seats must be an initiative-order prefix")
        if self.active_seat is not None and not isinstance(self.active_seat, Seat):
            raise TypeError("active_seat must be a Seat or None")
        unresolved = self.initiative_order[len(self.resolved_seats) :]
        if self.active_seat is not None and (
            not unresolved or self.active_seat != unresolved[0]
        ):
            raise ValueError("active_seat must be the first unresolved initiative entry")
        if (
            self.phase is Phase.CARD_RESOLUTION
            and self.active_seat is None
            and self.pending_initiative_choice is None
        ):
            raise ValueError("card resolution requires an active seat")
        if self.phase is not Phase.CARD_RESOLUTION and self.active_seat is not None:
            raise ValueError("active_seat is only valid during card resolution")
        if self.pending_action is not None and self.phase is not Phase.CARD_RESOLUTION:
            raise ValueError("pending_action is only valid during card resolution")
        if self.pending_movement is not None and self.pending_action is None:
            raise ValueError("pending movement requires an action")
        if self.pending_attack is not None:
            if self.phase is not Phase.CARD_RESOLUTION:
                raise ValueError("pending attack is only valid during card resolution")
            if self.pending_action is None or self.pending_action.kind != "primary":
                raise ValueError("pending attack requires a primary action")
            if self.active_seat != self.pending_attack.attacker_seat:
                raise ValueError("pending attack must belong to the active seat")
        if self.pending_initiative_choice is not None:
            if self.phase is not Phase.CARD_RESOLUTION:
                raise ValueError("initiative choice requires card resolution")
            if self.active_seat is not None:
                raise ValueError("initiative choice cannot coexist with an active seat")
        if self.pending_respawn is not None:
            if self.phase is not Phase.CARD_RESOLUTION:
                raise ValueError("pending respawn requires card resolution")
            if self.active_seat != self.pending_respawn.seat:
                raise ValueError("pending respawn must belong to the active seat")
            if self.controlled_seat != self.pending_respawn.seat:
                raise ValueError("pending respawn seat must control the room")
        if self.starting_crystal_life <= 0:
            raise ValueError("starting crystal life must be positive")
        if self.frontline_victory_marks <= 0:
            raise ValueError("frontline victory marks must be positive")
        if not isinstance(self.decision_coin, Team):
            raise TypeError("decision coin must be a Team")
        if tuple(team.team for team in self.teams) != (Team.BLUE, Team.RED):
            raise ValueError("teams must contain blue then red")
        if self.pending_captain_choice is not None and self.phase is Phase.GAME_OVER:
            raise ValueError("captain choice is not valid after game over")
        if self.pending_upgrade is not None:
            if self.phase is not Phase.UPGRADE:
                raise ValueError("pending upgrade requires upgrade phase")
        if self.resume_action_after_frontline and self.phase is not Phase.CARD_RESOLUTION:
            raise ValueError("frontline action resume requires card resolution")
        if self.winner is not None and self.phase is not Phase.GAME_OVER:
            raise ValueError("winner requires game over phase")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "revision": self.revision,
            "phase": self.phase.value,
            "controlled_seat": int(self.controlled_seat),
            "seats": [seat.to_dict() for seat in self.seats],
            "hero_ids": list(self.hero_ids),
            "initiative_order": [int(seat) for seat in self.initiative_order],
            "revealed_initiatives": list(self.revealed_initiatives),
            "resolved_seats": [int(seat) for seat in self.resolved_seats],
            "active_seat": (
                int(self.active_seat) if self.active_seat is not None else None
            ),
            "pending_action": (
                self.pending_action.to_dict() if self.pending_action is not None else None
            ),
            "pending_movement": self.pending_movement,
            "pending_attack": (
                self.pending_attack.to_dict()
                if self.pending_attack is not None
                else None
            ),
            "pending_initiative_choice": (
                self.pending_initiative_choice.to_dict()
                if self.pending_initiative_choice is not None
                else None
            ),
            "pending_respawn": (
                self.pending_respawn.to_dict()
                if self.pending_respawn is not None
                else None
            ),
            "board": [cell.to_dict() for cell in self.board],
            "units": [unit.to_dict() for unit in self.units],
            "starting_crystal_life": self.starting_crystal_life,
            "frontline_victory_marks": self.frontline_victory_marks,
            "decision_coin": self.decision_coin.value,
            "round_number": self.round_number,
            "turn_number": self.turn_number,
            "minions": [minion.to_dict() for minion in self.minions],
            "teams": [team.to_dict() for team in self.teams],
            "combat_region": self.combat_region,
            "round_end_step": self.round_end_step.value,
            "pending_captain_choice": (
                self.pending_captain_choice.to_dict()
                if self.pending_captain_choice is not None
                else None
            ),
            "pending_upgrade": (
                self.pending_upgrade.to_dict()
                if self.pending_upgrade is not None
                else None
            ),
            "resume_action_after_frontline": self.resume_action_after_frontline,
            "winner": self.winner.value if self.winner is not None else None,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "RoomState":
        expected_fields = {
            "revision",
            "phase",
            "controlled_seat",
            "seats",
            "hero_ids",
            "initiative_order",
            "revealed_initiatives",
            "resolved_seats",
            "active_seat",
            "pending_action",
            "pending_movement",
            "pending_attack",
            "pending_initiative_choice",
            "pending_respawn",
            "board",
            "units",
            "starting_crystal_life",
            "frontline_victory_marks",
            "decision_coin",
            "round_number",
            "turn_number",
            "minions",
            "teams",
            "combat_region",
            "round_end_step",
            "pending_captain_choice",
            "pending_upgrade",
            "resume_action_after_frontline",
            "winner",
        }
        actual_fields = set(value)
        if actual_fields not in (expected_fields, expected_fields - {"hero_ids"}):
            raise ValueError("room state has unexpected fields")
        raw_seats = value["seats"]
        if not isinstance(raw_seats, list) or len(raw_seats) != 4:
            raise ValueError("room state must contain four seats")
        seats = tuple(SeatState.from_dict(item) for item in raw_seats)
        raw_order = value["initiative_order"]
        if not isinstance(raw_order, list):
            raise ValueError("initiative_order must be a list")
        raw_resolved = value["resolved_seats"]
        if not isinstance(raw_resolved, list):
            raise ValueError("resolved_seats must be a list")
        raw_active_seat = value["active_seat"]
        raw_pending_action = value["pending_action"]
        return cls(
            revision=value["revision"],
            phase=Phase(value["phase"]),
            controlled_seat=Seat(value["controlled_seat"]),
            seats=seats,
            hero_ids=(
                tuple(value["hero_ids"])
                if "hero_ids" in value
                else DEFAULT_HERO_IDS
            ),
            initiative_order=tuple(Seat(seat) for seat in raw_order),
            revealed_initiatives=tuple(value["revealed_initiatives"]),
            resolved_seats=tuple(Seat(seat) for seat in raw_resolved),
            active_seat=(
                Seat(raw_active_seat) if raw_active_seat is not None else None
            ),
            pending_action=(
                ActionChoice.from_dict(raw_pending_action)
                if raw_pending_action is not None
                else None
            ),
            pending_movement=value["pending_movement"],
            pending_attack=(
                PendingAttack.from_dict(value["pending_attack"])
                if value["pending_attack"] is not None
                else None
            ),
            pending_initiative_choice=(
                PendingInitiativeChoice.from_dict(value["pending_initiative_choice"])
                if value["pending_initiative_choice"] is not None
                else None
            ),
            pending_respawn=(
                PendingRespawn.from_dict(value["pending_respawn"])
                if value["pending_respawn"] is not None
                else None
            ),
            board=tuple(BoardCell.from_dict(cell) for cell in value["board"]),
            units=tuple(UnitState.from_dict(unit) for unit in value["units"]),
            starting_crystal_life=value["starting_crystal_life"],
            frontline_victory_marks=value["frontline_victory_marks"],
            decision_coin=Team(value["decision_coin"]),
            round_number=value["round_number"],
            turn_number=value["turn_number"],
            minions=tuple(MinionState.from_dict(item) for item in value["minions"]),
            teams=tuple(TeamState.from_dict(item) for item in value["teams"]),
            combat_region=value["combat_region"],
            round_end_step=RoundEndStep(value["round_end_step"]),
            pending_captain_choice=(
                PendingCaptainChoice.from_dict(value["pending_captain_choice"])
                if value["pending_captain_choice"] is not None
                else None
            ),
            pending_upgrade=(
                PendingUpgrade.from_dict(value["pending_upgrade"])
                if value["pending_upgrade"] is not None
                else None
            ),
            resume_action_after_frontline=value["resume_action_after_frontline"],
            winner=Team(value["winner"]) if value["winner"] is not None else None,
        )


def create_initial_room_state(
    *,
    starting_crystal_life: int = 7,
    frontline_victory_marks: int = 3,
    decision_coin: Team | None = None,
    hero_ids: RoomHeroIds = DEFAULT_HERO_IDS,
) -> RoomState:
    return RoomState(
        revision=0,
        phase=Phase.CARD_SELECTION,
        controlled_seat=Seat.ZERO,
        seats=(
            SeatState(Seat.ZERO, Team.BLUE),
            SeatState(Seat.ONE, Team.RED),
            SeatState(Seat.TWO, Team.BLUE),
            SeatState(Seat.THREE, Team.RED),
        ),
        hero_ids=hero_ids,
        starting_crystal_life=starting_crystal_life,
        frontline_victory_marks=frontline_victory_marks,
        decision_coin=decision_coin or choice((Team.BLUE, Team.RED)),
        teams=(
            TeamState(Team.BLUE, crystal_life=starting_crystal_life, captain_seat=Seat.ZERO),
            TeamState(Team.RED, crystal_life=starting_crystal_life, captain_seat=Seat.ONE),
        ),
        combat_region="mid",
    )

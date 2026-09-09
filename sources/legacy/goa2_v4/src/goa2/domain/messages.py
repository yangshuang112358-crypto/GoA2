"""Commands accepted by the engine and events emitted by it."""

from __future__ import annotations

from abc import ABC, abstractmethod
from collections.abc import Mapping
from dataclasses import dataclass
from typing import ClassVar, TypeAlias

from .model import ActionChoice, ActionProfile, JsonValue, Phase, Seat, Team

RevealCardData: TypeAlias = tuple[Seat, str, int]
RevealProfileData: TypeAlias = tuple[Seat, ActionProfile]


def _validate_card_id(card_id: str) -> None:
    if not isinstance(card_id, str) or not card_id:
        raise ValueError("card_id must be a non-empty string")


def _validate_reveal_cards(cards: tuple[RevealCardData, ...]) -> None:
    if not isinstance(cards, tuple) or len(cards) != 4:
        raise ValueError("cards must contain exactly four reveal entries")
    for entry in cards:
        if not isinstance(entry, tuple) or len(entry) != 3:
            raise ValueError("each reveal entry must be (seat, card_id, initiative)")
        seat, card_id, initiative = entry
        if not isinstance(seat, Seat):
            raise TypeError("reveal seat must be a Seat")
        _validate_card_id(card_id)
        if not isinstance(initiative, int) or isinstance(initiative, bool):
            raise TypeError("initiative must be an integer")


def _cards_to_json(cards: tuple[RevealCardData, ...]) -> list[JsonValue]:
    return [
        {"seat": int(seat), "card_id": card_id, "initiative": initiative}
        for seat, card_id, initiative in cards
    ]


def _cards_from_json(value: JsonValue) -> tuple[RevealCardData, ...]:
    if not isinstance(value, list):
        raise ValueError("cards must be a list")
    cards: list[RevealCardData] = []
    for item in value:
        if not isinstance(item, dict) or set(item) != {
            "seat",
            "card_id",
            "initiative",
        }:
            raise ValueError("reveal card entry has unexpected fields")
        cards.append(
            (Seat(item["seat"]), item["card_id"], item["initiative"])
        )
    result = tuple(cards)
    _validate_reveal_cards(result)
    return result


def _profiles_to_json(profiles: tuple[RevealProfileData, ...]) -> list[JsonValue]:
    return [
        {"seat": int(seat), "profile": profile.to_dict()}
        for seat, profile in profiles
    ]


def _profiles_from_json(value: JsonValue) -> tuple[RevealProfileData, ...]:
    if not isinstance(value, list) or len(value) != 4:
        raise ValueError("profiles must contain four entries")
    result = []
    for item in value:
        if not isinstance(item, dict) or set(item) != {"seat", "profile"}:
            raise ValueError("profile entry has unexpected fields")
        result.append((Seat(item["seat"]), ActionProfile.from_dict(item["profile"])))
    return tuple(result)


@dataclass(frozen=True, slots=True)
class Command(ABC):
    expected_revision: int

    def __post_init__(self) -> None:
        if not isinstance(self.expected_revision, int) or isinstance(
            self.expected_revision, bool
        ):
            raise TypeError("expected_revision must be an integer")
        if self.expected_revision < 0:
            raise ValueError("expected_revision must be non-negative")

    @abstractmethod
    def to_dict(self) -> dict[str, JsonValue]:
        raise NotImplementedError


@dataclass(frozen=True, slots=True)
class SwitchControlledSeat(Command):
    kind: ClassVar[str] = "switch_controlled_seat"
    target_seat: Seat

    def __post_init__(self) -> None:
        super(SwitchControlledSeat, self).__post_init__()
        if not isinstance(self.target_seat, Seat):
            raise TypeError("target_seat must be a Seat")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "target_seat": int(self.target_seat),
        }


@dataclass(frozen=True, slots=True)
class ResetRoom(Command):
    kind: ClassVar[str] = "reset_room"

    def to_dict(self) -> dict[str, JsonValue]:
        return {"type": self.kind, "expected_revision": self.expected_revision}


@dataclass(frozen=True, slots=True)
class SelectCard(Command):
    kind: ClassVar[str] = "select_card"
    seat: Seat
    card_id: str

    def __post_init__(self) -> None:
        super(SelectCard, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")
        _validate_card_id(self.card_id)

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "card_id": self.card_id,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "SelectCard":
        if set(value) != {"type", "expected_revision", "seat", "card_id"}:
            raise ValueError("select card command has unexpected fields")
        if value["type"] != cls.kind:
            raise ValueError("invalid select card command type")
        return cls(
            expected_revision=value["expected_revision"],
            seat=Seat(value["seat"]),
            card_id=value["card_id"],
        )


@dataclass(frozen=True, slots=True)
class ConfirmSelection(Command):
    kind: ClassVar[str] = "confirm_selection"
    seat: Seat

    def __post_init__(self) -> None:
        super(ConfirmSelection, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "ConfirmSelection":
        if set(value) != {"type", "expected_revision", "seat"}:
            raise ValueError("confirm selection command has unexpected fields")
        if value["type"] != cls.kind:
            raise ValueError("invalid confirm selection command type")
        return cls(
            expected_revision=value["expected_revision"],
            seat=Seat(value["seat"]),
        )


@dataclass(frozen=True, slots=True)
class RevealCards(Command):
    kind: ClassVar[str] = "reveal_cards"
    cards: tuple[RevealCardData, ...]
    profiles: tuple[RevealProfileData, ...]

    def __post_init__(self) -> None:
        super(RevealCards, self).__post_init__()
        _validate_reveal_cards(self.cards)
        if {seat for seat, _ in self.profiles} != set(Seat):
            raise ValueError("profiles must contain each seat exactly once")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "cards": _cards_to_json(self.cards),
            "profiles": _profiles_to_json(self.profiles),
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "RevealCards":
        if set(value) != {"type", "expected_revision", "cards", "profiles"}:
            raise ValueError("reveal cards command has unexpected fields")
        if value["type"] != cls.kind:
            raise ValueError("invalid reveal cards command type")
        return cls(
            expected_revision=value["expected_revision"],
            cards=_cards_from_json(value["cards"]),
            profiles=_profiles_from_json(value["profiles"]),
        )


@dataclass(frozen=True, slots=True)
class ChooseResolutionAction(Command):
    kind: ClassVar[str] = "choose_resolution_action"
    seat: Seat
    choice: ActionChoice

    def __post_init__(self) -> None:
        super(ChooseResolutionAction, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")
        if not isinstance(self.choice, ActionChoice):
            raise TypeError("choice must be an ActionChoice")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "choice": self.choice.to_dict(),
        }


@dataclass(frozen=True, slots=True)
class CancelResolutionAction(Command):
    kind: ClassVar[str] = "cancel_resolution_action"
    seat: Seat

    def __post_init__(self) -> None:
        super(CancelResolutionAction, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
        }


@dataclass(frozen=True, slots=True)
class MoveHero(Command):
    kind: ClassVar[str] = "move_hero"
    seat: Seat
    x: int
    y: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind, "expected_revision": self.expected_revision,
            "seat": int(self.seat), "x": self.x, "y": self.y,
        }


@dataclass(frozen=True, slots=True)
class DeclareBasicAttack(Command):
    kind: ClassVar[str] = "declare_basic_attack"
    seat: Seat
    target_id: str

    def __post_init__(self) -> None:
        super(DeclareBasicAttack, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")
        if not isinstance(self.target_id, str) or not self.target_id:
            raise ValueError("target_id must be a non-empty string")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "target_id": self.target_id,
        }


@dataclass(frozen=True, slots=True)
class ResolveBasicDefense(Command):
    kind: ClassVar[str] = "resolve_basic_defense"
    seat: Seat
    card_id: str | None
    defense_value: int | None
    exclamation: bool = False

    def __post_init__(self) -> None:
        super(ResolveBasicDefense, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")
        if self.card_id is None:
            if self.defense_value is not None or self.exclamation:
                raise ValueError("declining defense cannot include defense data")
        elif self.defense_value is None:
            raise ValueError("defense card requires a defense value")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "card_id": self.card_id,
            "defense_value": self.defense_value,
            "exclamation": self.exclamation,
        }


@dataclass(frozen=True, slots=True)
class ResolveRespawn(Command):
    kind: ClassVar[str] = "resolve_respawn"
    seat: Seat
    x: int
    y: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "x": self.x,
            "y": self.y,
        }


@dataclass(frozen=True, slots=True)
class ChooseCardUpgrade(Command):
    kind: ClassVar[str] = "choose_card_upgrade"
    seat: Seat
    color: str
    current_card_id: str
    chosen_card_id: str
    unchosen_card_id: str
    passive_index: int

    def __post_init__(self) -> None:
        super(ChooseCardUpgrade, self).__post_init__()
        if self.color not in {"red", "green", "blue"}:
            raise ValueError("upgrade color must be red, green, or blue")
        if not self.current_card_id or not self.chosen_card_id or not self.unchosen_card_id:
            raise ValueError("upgrade card ids must be non-empty")
        if not 0 <= self.passive_index < 6:
            raise ValueError("passive index must be from 0 to 5")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "color": self.color,
            "current_card_id": self.current_card_id,
            "chosen_card_id": self.chosen_card_id,
            "unchosen_card_id": self.unchosen_card_id,
            "passive_index": self.passive_index,
        }


@dataclass(frozen=True, slots=True)
class BeginRoundEnd(Command):
    kind: ClassVar[str] = "begin_round_end"

    def to_dict(self) -> dict[str, JsonValue]:
        return {"type": self.kind, "expected_revision": self.expected_revision}


@dataclass(frozen=True, slots=True)
class ResolveCaptainChoice(Command):
    kind: ClassVar[str] = "resolve_captain_choice"
    seat: Seat
    choice_id: str
    candidate_id: str

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "choice_id": self.choice_id,
            "candidate_id": self.candidate_id,
        }


@dataclass(frozen=True, slots=True)
class ResolveInitiativeChoice(Command):
    kind: ClassVar[str] = "resolve_initiative_choice"
    seat: Seat
    choice_id: str
    chosen_seat: Seat

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "choice_id": self.choice_id,
            "chosen_seat": int(self.chosen_seat),
        }


@dataclass(frozen=True, slots=True)
class CompletePendingAction(Command):
    kind: ClassVar[str] = "complete_pending_action"
    seat: Seat

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
        }


@dataclass(frozen=True, slots=True)
class DebugTeleportHero(Command):
    kind: ClassVar[str] = "debug_teleport_hero"
    seat: Seat
    x: int
    y: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "x": self.x,
            "y": self.y,
        }


@dataclass(frozen=True, slots=True)
class DebugEnterRoundEnd(Command):
    kind: ClassVar[str] = "debug_enter_round_end"

    def to_dict(self) -> dict[str, JsonValue]:
        return {"type": self.kind, "expected_revision": self.expected_revision}


@dataclass(frozen=True, slots=True)
class DebugRemoveMinion(Command):
    kind: ClassVar[str] = "debug_remove_minion"
    seat: Seat
    minion_id: str

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "minion_id": self.minion_id,
        }


def _validate_debug_value(value: int, field_name: str) -> None:
    if not isinstance(value, int) or isinstance(value, bool):
        raise TypeError(f"{field_name} must be an integer")
    if value < 0:
        raise ValueError(f"{field_name} must be non-negative")


@dataclass(frozen=True, slots=True)
class DebugSetSeatCoins(Command):
    kind: ClassVar[str] = "debug_set_seat_coins"
    seat: Seat
    coins: int

    def __post_init__(self) -> None:
        super(DebugSetSeatCoins, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")
        _validate_debug_value(self.coins, "coins")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
            "coins": self.coins,
        }


@dataclass(frozen=True, slots=True)
class DebugSetCrystalLife(Command):
    kind: ClassVar[str] = "debug_set_crystal_life"
    team: Team
    crystal_life: int

    def __post_init__(self) -> None:
        super(DebugSetCrystalLife, self).__post_init__()
        if not isinstance(self.team, Team):
            raise TypeError("team must be a Team")
        _validate_debug_value(self.crystal_life, "crystal_life")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "team": self.team.value,
            "crystal_life": self.crystal_life,
        }


@dataclass(frozen=True, slots=True)
class DebugSetFrontlineMarks(Command):
    kind: ClassVar[str] = "debug_set_frontline_marks"
    team: Team
    frontline_marks: int

    def __post_init__(self) -> None:
        super(DebugSetFrontlineMarks, self).__post_init__()
        if not isinstance(self.team, Team):
            raise TypeError("team must be a Team")
        _validate_debug_value(self.frontline_marks, "frontline_marks")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "team": self.team.value,
            "frontline_marks": self.frontline_marks,
        }


@dataclass(frozen=True, slots=True)
class DebugDefeatHero(Command):
    kind: ClassVar[str] = "debug_defeat_hero"
    seat: Seat

    def __post_init__(self) -> None:
        super(DebugDefeatHero, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
        }


@dataclass(frozen=True, slots=True)
class DebugResetMinions(Command):
    kind: ClassVar[str] = "debug_reset_minions"

    def to_dict(self) -> dict[str, JsonValue]:
        return {"type": self.kind, "expected_revision": self.expected_revision}


@dataclass(frozen=True, slots=True)
class DebugAdvanceFrontline(Command):
    kind: ClassVar[str] = "debug_advance_frontline"
    team: Team

    def __post_init__(self) -> None:
        super(DebugAdvanceFrontline, self).__post_init__()
        if not isinstance(self.team, Team):
            raise TypeError("team must be a Team")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "team": self.team.value,
        }


@dataclass(frozen=True, slots=True)
class ForceConfirmSelections(Command):
    kind: ClassVar[str] = "force_confirm_selections"
    default_cards: Mapping[int, str]

    def __post_init__(self) -> None:
        super(ForceConfirmSelections, self).__post_init__()
        if not isinstance(self.default_cards, Mapping):
            raise TypeError("default_cards must be a mapping")
        if set(self.default_cards) != {0, 1, 2, 3}:
            raise ValueError("default_cards must contain seats 0 through 3")
        for card_id in self.default_cards.values():
            _validate_card_id(card_id)

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "default_cards": {
                str(seat): card_id for seat, card_id in self.default_cards.items()
            },
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "ForceConfirmSelections":
        if set(value) != {"type", "expected_revision", "default_cards"}:
            raise ValueError("force confirm command has unexpected fields")
        if value["type"] != cls.kind:
            raise ValueError("invalid force confirm command type")
        raw_cards = value["default_cards"]
        if not isinstance(raw_cards, dict):
            raise ValueError("default_cards must be an object")
        return cls(
            expected_revision=value["expected_revision"],
            default_cards={int(seat): card_id for seat, card_id in raw_cards.items()},
        )


@dataclass(frozen=True, slots=True)
class SkipAction(Command):
    kind: ClassVar[str] = "skip_action"
    seat: Seat

    def __post_init__(self) -> None:
        super(SkipAction, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "expected_revision": self.expected_revision,
            "seat": int(self.seat),
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "SkipAction":
        if set(value) != {"type", "expected_revision", "seat"}:
            raise ValueError("skip action command has unexpected fields")
        if value["type"] != cls.kind:
            raise ValueError("invalid skip action command type")
        return cls(
            expected_revision=value["expected_revision"],
            seat=Seat(value["seat"]),
        )


@dataclass(frozen=True, slots=True)
class Event(ABC):
    revision: int

    def __post_init__(self) -> None:
        if not isinstance(self.revision, int) or isinstance(self.revision, bool):
            raise TypeError("revision must be an integer")
        if self.revision < 0:
            raise ValueError("revision must be non-negative")

    @abstractmethod
    def to_dict(self) -> dict[str, JsonValue]:
        raise NotImplementedError


@dataclass(frozen=True, slots=True)
class ControlledSeatSwitched(Event):
    kind: ClassVar[str] = "controlled_seat_switched"
    previous_seat: Seat
    controlled_seat: Seat

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "previous_seat": int(self.previous_seat),
            "controlled_seat": int(self.controlled_seat),
        }


@dataclass(frozen=True, slots=True)
class RoomReset(Event):
    kind: ClassVar[str] = "room_reset"
    phase: Phase
    controlled_seat: Seat

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "phase": self.phase.value,
            "controlled_seat": int(self.controlled_seat),
        }


@dataclass(frozen=True, slots=True)
class CardSelected(Event):
    kind: ClassVar[str] = "card_selected"
    seat: Seat
    card_id: str

    def __post_init__(self) -> None:
        super(CardSelected, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")
        _validate_card_id(self.card_id)

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "card_id": self.card_id,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "CardSelected":
        if set(value) != {"type", "revision", "seat", "card_id"}:
            raise ValueError("card selected event has unexpected fields")
        if value["type"] != cls.kind:
            raise ValueError("invalid card selected event type")
        return cls(
            revision=value["revision"],
            seat=Seat(value["seat"]),
            card_id=value["card_id"],
        )


@dataclass(frozen=True, slots=True)
class SelectionConfirmed(Event):
    kind: ClassVar[str] = "selection_confirmed"
    seat: Seat
    phase: Phase

    def __post_init__(self) -> None:
        super(SelectionConfirmed, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")
        if not isinstance(self.phase, Phase):
            raise TypeError("phase must be a Phase")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "phase": self.phase.value,
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "SelectionConfirmed":
        if set(value) != {"type", "revision", "seat", "phase"}:
            raise ValueError("selection confirmed event has unexpected fields")
        if value["type"] != cls.kind:
            raise ValueError("invalid selection confirmed event type")
        return cls(
            revision=value["revision"],
            seat=Seat(value["seat"]),
            phase=Phase(value["phase"]),
        )


@dataclass(frozen=True, slots=True)
class CardsRevealed(Event):
    kind: ClassVar[str] = "cards_revealed"
    cards: tuple[RevealCardData, ...]
    initiative_order: tuple[Seat, ...]
    active_seat: Seat | None
    phase: Phase
    profiles: tuple[RevealProfileData, ...]

    def __post_init__(self) -> None:
        super(CardsRevealed, self).__post_init__()
        _validate_reveal_cards(self.cards)
        if (
            not isinstance(self.initiative_order, tuple)
            or len(self.initiative_order) != 4
            or any(not isinstance(seat, Seat) for seat in self.initiative_order)
        ):
            raise ValueError("initiative_order must contain four seats")
        if self.active_seat is not None and not isinstance(self.active_seat, Seat):
            raise TypeError("active_seat must be a Seat or None")
        if self.active_seat is not None and self.active_seat != self.initiative_order[0]:
            raise ValueError("active_seat must be the first initiative entry")
        if not isinstance(self.phase, Phase):
            raise TypeError("phase must be a Phase")
        if {seat for seat, _ in self.profiles} != set(Seat):
            raise ValueError("profiles must contain each seat exactly once")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "cards": _cards_to_json(self.cards),
            "initiative_order": [int(seat) for seat in self.initiative_order],
            "active_seat": (
                int(self.active_seat) if self.active_seat is not None else None
            ),
            "phase": self.phase.value,
            "profiles": _profiles_to_json(self.profiles),
        }

    @classmethod
    def from_dict(cls, value: dict[str, JsonValue]) -> "CardsRevealed":
        expected_fields = {
            "type",
            "revision",
            "cards",
            "initiative_order",
            "active_seat",
            "phase",
            "profiles",
        }
        if set(value) != expected_fields:
            raise ValueError("cards revealed event has unexpected fields")
        if value["type"] != cls.kind:
            raise ValueError("invalid cards revealed event type")
        raw_order = value["initiative_order"]
        if not isinstance(raw_order, list):
            raise ValueError("initiative_order must be a list")
        return cls(
            revision=value["revision"],
            cards=_cards_from_json(value["cards"]),
            initiative_order=tuple(Seat(seat) for seat in raw_order),
            active_seat=(
                Seat(value["active_seat"])
                if value["active_seat"] is not None
                else None
            ),
            phase=Phase(value["phase"]),
            profiles=_profiles_from_json(value["profiles"]),
        )


@dataclass(frozen=True, slots=True)
class SelectionsForceConfirmed(Event):
    kind: ClassVar[str] = "selections_force_confirmed"
    phase: Phase

    def __post_init__(self) -> None:
        super(SelectionsForceConfirmed, self).__post_init__()
        if not isinstance(self.phase, Phase):
            raise TypeError("phase must be a Phase")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "phase": self.phase.value,
        }


@dataclass(frozen=True, slots=True)
class ActionSkipped(Event):
    kind: ClassVar[str] = "action_skipped"
    seat: Seat
    next_active_seat: Seat | None
    phase: Phase

    def __post_init__(self) -> None:
        super(ActionSkipped, self).__post_init__()
        if not isinstance(self.seat, Seat):
            raise TypeError("seat must be a Seat")
        if self.next_active_seat is not None and not isinstance(
            self.next_active_seat, Seat
        ):
            raise TypeError("next_active_seat must be a Seat or None")
        if not isinstance(self.phase, Phase):
            raise TypeError("phase must be a Phase")

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "next_active_seat": (
                int(self.next_active_seat)
                if self.next_active_seat is not None
                else None
            ),
            "phase": self.phase.value,
        }


@dataclass(frozen=True, slots=True)
class ResolutionActionChosen(Event):
    kind: ClassVar[str] = "resolution_action_chosen"
    seat: Seat
    choice: ActionChoice

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "choice": self.choice.to_dict(),
        }


@dataclass(frozen=True, slots=True)
class ResolutionActionCancelled(Event):
    kind: ClassVar[str] = "resolution_action_cancelled"
    seat: Seat

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
        }


@dataclass(frozen=True, slots=True)
class HeroMoved(Event):
    kind: ClassVar[str] = "hero_moved"
    seat: Seat
    source: tuple[int, int]
    destination: tuple[int, int]
    path: tuple[tuple[int, int], ...]
    cost: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind, "revision": self.revision, "seat": int(self.seat),
            "source": list(self.source), "destination": list(self.destination),
            "path": [list(point) for point in self.path], "cost": self.cost,
        }


@dataclass(frozen=True, slots=True)
class HeroFastMoved(Event):
    kind: ClassVar[str] = "hero_fast_moved"
    seat: Seat
    source: tuple[int, int]
    destination: tuple[int, int]
    source_region: str
    destination_region: str

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "source": list(self.source),
            "destination": list(self.destination),
            "source_region": self.source_region,
            "destination_region": self.destination_region,
        }


@dataclass(frozen=True, slots=True)
class BasicAttackDeclared(Event):
    kind: ClassVar[str] = "basic_attack_declared"
    seat: Seat
    card_id: str
    target_id: str
    target_kind: str
    base_attack: int
    support_bonus: int
    defense_reduction: int
    attack_value: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "card_id": self.card_id,
            "target_id": self.target_id,
            "target_kind": self.target_kind,
            "base_attack": self.base_attack,
            "support_bonus": self.support_bonus,
            "defense_reduction": self.defense_reduction,
            "attack_value": self.attack_value,
        }


@dataclass(frozen=True, slots=True)
class BasicDefenseResolved(Event):
    kind: ClassVar[str] = "basic_defense_resolved"
    attacker_seat: Seat
    defender_seat: Seat
    card_id: str | None
    defense_value: int | None
    attack_value: int
    defended: bool
    defeated: bool

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "attacker_seat": int(self.attacker_seat),
            "defender_seat": int(self.defender_seat),
            "card_id": self.card_id,
            "defense_value": self.defense_value,
            "attack_value": self.attack_value,
            "defended": self.defended,
            "defeated": self.defeated,
        }


@dataclass(frozen=True, slots=True)
class HeroRespawned(Event):
    kind: ClassVar[str] = "hero_respawned"
    seat: Seat
    x: int
    y: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "x": self.x,
            "y": self.y,
        }


@dataclass(frozen=True, slots=True)
class MinionDefeated(Event):
    kind: ClassVar[str] = "minion_defeated"
    attacker_seat: Seat
    minion_id: str
    attack_value: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "attacker_seat": int(self.attacker_seat),
            "minion_id": self.minion_id,
            "attack_value": self.attack_value,
        }


@dataclass(frozen=True, slots=True)
class RoundEndStarted(Event):
    kind: ClassVar[str] = "round_end_started"
    step: str

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "step": self.step,
        }


@dataclass(frozen=True, slots=True)
class CaptainChoiceResolved(Event):
    kind: ClassVar[str] = "captain_choice_resolved"
    seat: Seat
    choice_kind: str
    candidate_id: str

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "choice_kind": self.choice_kind,
            "candidate_id": self.candidate_id,
        }


@dataclass(frozen=True, slots=True)
class InitiativeChoiceResolved(Event):
    kind: ClassVar[str] = "initiative_choice_resolved"
    captain_seat: Seat
    chosen_seat: Seat
    initiative: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "captain_seat": int(self.captain_seat),
            "chosen_seat": int(self.chosen_seat),
            "initiative": self.initiative,
        }


@dataclass(frozen=True, slots=True)
class PendingActionCompleted(Event):
    kind: ClassVar[str] = "data_only_action_completed"
    seat: Seat
    card_id: str
    primary_family: str
    next_active_seat: Seat | None
    phase: str

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "card_id": self.card_id,
            "primary_family": self.primary_family,
            "next_active_seat": (
                int(self.next_active_seat)
                if self.next_active_seat is not None
                else None
            ),
            "phase": self.phase,
        }


@dataclass(frozen=True, slots=True)
class HeroDebugTeleported(Event):
    kind: ClassVar[str] = "hero_debug_teleported"
    seat: Seat
    source: tuple[int, int]
    destination: tuple[int, int]

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "source": list(self.source),
            "destination": list(self.destination),
        }


@dataclass(frozen=True, slots=True)
class MinionDebugRemoved(Event):
    kind: ClassVar[str] = "minion_debug_removed"
    seat: Seat
    minion_id: str

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "minion_id": self.minion_id,
        }


@dataclass(frozen=True, slots=True)
class SeatCoinsDebugSet(Event):
    kind: ClassVar[str] = "seat_coins_debug_set"
    seat: Seat
    coins: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "coins": self.coins,
        }


@dataclass(frozen=True, slots=True)
class CrystalLifeDebugSet(Event):
    kind: ClassVar[str] = "crystal_life_debug_set"
    team: Team
    crystal_life: int
    winner: Team | None

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "team": self.team.value,
            "crystal_life": self.crystal_life,
            "winner": self.winner.value if self.winner is not None else None,
        }


@dataclass(frozen=True, slots=True)
class FrontlineMarksDebugSet(Event):
    kind: ClassVar[str] = "frontline_marks_debug_set"
    team: Team
    frontline_marks: int
    winner: Team | None

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "team": self.team.value,
            "frontline_marks": self.frontline_marks,
            "winner": self.winner.value if self.winner is not None else None,
        }


@dataclass(frozen=True, slots=True)
class HeroDebugDefeated(Event):
    kind: ClassVar[str] = "hero_debug_defeated"
    seat: Seat
    crystal_damage: int
    winner: Team | None

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "seat": int(self.seat),
            "crystal_damage": self.crystal_damage,
            "winner": self.winner.value if self.winner is not None else None,
        }


@dataclass(frozen=True, slots=True)
class MinionsDebugReset(Event):
    kind: ClassVar[str] = "minions_debug_reset"
    combat_region: str
    minion_count: int

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "combat_region": self.combat_region,
            "minion_count": self.minion_count,
        }


@dataclass(frozen=True, slots=True)
class FrontlineDebugAdvanced(Event):
    kind: ClassVar[str] = "frontline_debug_advanced"
    team: Team
    previous_region: str
    combat_region: str | None
    frontline_marks: int
    winner: Team | None

    def to_dict(self) -> dict[str, JsonValue]:
        return {
            "type": self.kind,
            "revision": self.revision,
            "team": self.team.value,
            "previous_region": self.previous_region,
            "combat_region": self.combat_region,
            "frontline_marks": self.frontline_marks,
            "winner": self.winner.value if self.winner is not None else None,
        }

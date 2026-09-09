"""Public rule-engine API."""

from .frontline import (
    legal_minion_removal_ids,
    minion_count_difference,
    spawn_minions_for_region,
    winner_from_team_state,
)
from .modifiers import modified_value, passive_bonus, passive_type_index
from .reducer import (
    IllegalCommand,
    Reduction,
    adjacent_regions,
    attack_minion_modifiers,
    basic_attack_targets,
    fast_move_targets,
    reachable_cells,
    reduce_room,
)

__all__ = [
    "IllegalCommand",
    "Reduction",
    "adjacent_regions",
    "attack_minion_modifiers",
    "basic_attack_targets",
    "fast_move_targets",
    "legal_minion_removal_ids",
    "minion_count_difference",
    "reachable_cells",
    "reduce_room",
    "spawn_minions_for_region",
    "modified_value",
    "passive_bonus",
    "passive_type_index",
    "winner_from_team_state",
]

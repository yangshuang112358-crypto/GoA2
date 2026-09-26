#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    // These roles exist only inside a minion battle; they never change a unit's kind.
    internal static class MinionBattleParticipantRules
    {
        internal static int Count(GameState state, List<BattleHeroContribution>? roles, Team team) =>
            state.Units.Count(u => (u.Kind == "melee" || u.Kind == "ranged" || u.Kind == "heavy") && u.Team == team) + ForTeam(state,roles,team).Sum(r=>r.Count);
        internal static List<BattleHeroContribution>? Capture(ContentCatalog catalog, GameState state)
        {
            if (state.EngineVersion < 59) return null;
            var roles = new List<BattleHeroContribution>();
            foreach (var hero in state.Units.Where(u => u.Kind == "hero" && u.Seat.HasValue).OrderBy(u => u.Seat))
            {
                var program = UltimateRules.OwnedProgram(catalog, state, hero.Seat!.Value);
                if (program?.Trigger != UltimateTrigger.MinionBattleParticipant) continue;
                // D-042: location is unrestricted while the hero is on the board.
                roles.Add(new BattleHeroContribution { UnitId = hero.Id, ControllerSeat = hero.Seat.Value,
                    SourceCardId = state.Players[hero.Seat.Value].PurpleCardId!, Count = program.BattleMinionCount,
                    ProgramId = program.Id, ProgramVersion = program.Version });
            }
            return roles.Count == 0 ? null : roles;
        }

        internal static List<BattleHeroContribution> ForTeam(GameState state, List<BattleHeroContribution>? roles, Team? team) =>
            state.EngineVersion < 59 || roles == null ? new List<BattleHeroContribution>() :
            roles.Where(r => state.Units.Any(u => u.Id == r.UnitId && u.Kind == "hero" && u.Seat == r.ControllerSeat && u.Team == team)).ToList();

        internal static List<string> RemovalCandidates(GameState state, List<BattleHeroContribution>? roles, Team? losingTeam)
        {
            var heroes = ForTeam(state, roles, losingTeam);
            var legal = GameRules.LegalMinionRemovals(state);
            return state.Units.Where(u => u.Team == losingTeam && legal.Contains(u.Id) && (u.Kind != "heavy" || heroes.Count == 0))
                .Select(u => u.Id).Concat(heroes.Select(h => h.UnitId)).OrderBy(id => id, System.StringComparer.Ordinal).ToList();
        }
    }
}

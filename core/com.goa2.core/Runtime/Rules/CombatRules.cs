#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public static class CombatRules
    {
        public static bool HasPrimaryProgram(CardDefinition card, int engineVersion = GameState.CurrentEngineVersion) => CardPrograms.Primary(card,engineVersion) != null;
        public static bool HasDefenseProgram(CardDefinition card, int engineVersion = GameState.CurrentEngineVersion) => CardPrograms.Defense(card,engineVersion) != null;
        public static List<string> AttackTargets(ContentCatalog catalog, GameState state, int seat)
        {
            var result = new List<string>();
            if (seat < 0 || seat > 3 || state.ActiveSeat != seat ||
                !(state.Phase == Phase.Action && state.Execution == null && state.Pending == null ||
                  state.Pending?.Kind == "attack_target" && state.Pending.ChooserSeat == seat && state.Execution != null)) return result;
            var instance = state.Players[seat].Cards.SingleOrDefault(c => c.Zone == CardZone.PlayedUnresolved);
            var source = state.Units.SingleOrDefault(u => u.Seat == seat);
            if (instance == null || source == null) return result;
            var card = catalog.Card(instance.CardId); var program = CardPrograms.Primary(card,state.EngineVersion);
            if (program == null || !program.Instructions.Contains(InstructionKind.ChooseAttackTarget)) return result;
            return Targets(catalog, state, source, card, program);
        }
        internal static List<string> Targets(ContentCatalog catalog, GameState state, UnitState source, CardDefinition card, PrimaryProgram program)
        {
            int distance = program.AdjacentAttack ? 1 : (card.SubtypeValue ?? 0) + state.Players[source.Seat!.Value].RangedBonus;
            var removable = new HashSet<string>(GameRules.LegalMinionRemovals(state));
            return state.Units.Where(u => u.Team != source.Team && u.Position.Distance(source.Position) >= program.MinimumDistance &&
                    u.Position.Distance(source.Position) <= distance && (u.Kind == "hero" || !program.OnlyHeroes && removable.Contains(u.Id)) &&
                    EffectRules.CanBeAttacked(state,source,u,card.Subtype=="远程"))
                .Select(u => u.Id).OrderBy(id => id, System.StringComparer.Ordinal).ToList();
        }
        public static List<DefenseOption> DefenseOptions(ContentCatalog catalog, GameState state, int seat)
        {
            var result = new List<DefenseOption>(); var attack = state.Execution?.Attack;
            if (attack == null || state.Pending?.Kind != "defense" || state.Pending.ChooserSeat != seat || seat != attack.DefenderSeat) return result;
            var defender = state.Units.SingleOrDefault(u => u.Seat == seat);
            var attacker = state.Units.SingleOrDefault(u => u.Seat == attack.AttackerSeat);
            if (defender == null || attacker == null) return result;
            foreach (var instance in state.Players[seat].Cards.Where(c => c.Zone == CardZone.InHand))
            {
                var card = catalog.Card(instance.CardId); var primary = CardPrograms.Defense(card,state.EngineVersion);
                if (card.PrimaryFamily == "defense")
                {
                    if (primary == null) continue;
                    if (primary.Block && attack.Unblockable || primary.RequiresRanged && !attack.Ranged ||
                        attacker.Position.Distance(defender.Position) < primary.MinimumDistance) continue;
                    result.Add(new DefenseOption { CardId = card.Id, Primary = true, Block = primary.Block, IgnoresMinions = primary.IgnoresMinions,
                        Assessment = CombatMath.Defense(attack, card.PrimaryValue, state.Players[seat].DefenseBonus, primary.IgnoresMinions, primary.Block) });
                }
                else if (card.SecondaryDefense.HasValue)
                    result.Add(new DefenseOption { CardId = card.Id, Assessment = CombatMath.Defense(attack, card.SecondaryDefense.Value, state.Players[seat].DefenseBonus) });
            }
            return result;
        }
        public static List<string> UnimplementedDefenses(ContentCatalog catalog, GameState state, int seat)
        {
            if (state.Pending?.Kind != "defense" || state.Pending.ChooserSeat != seat) return new List<string>();
            return state.Players[seat].Cards.Where(c => c.Zone == CardZone.InHand).Select(c => catalog.Card(c.CardId))
                .Where(c => c.PrimaryFamily == "defense" && CardPrograms.Defense(c,state.EngineVersion) == null).Select(c => c.Id).ToList();
        }
    }
}

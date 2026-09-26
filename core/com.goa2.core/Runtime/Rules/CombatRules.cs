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
        public static CardAttackModifier CardTextModifier(ContentCatalog catalog,GameState state,CardDefinition card,UnitState source,UnitState target)
        {
            var result=new CardAttackModifier();
            var program=CardPrograms.Primary(card,state.EngineVersion);
            if (program==null || program.AttackBonusValue==0) return result;
            if (program.AttackBonusKind==AttackBonusKind.TargetUsedAttack)
            {
                if(target.Kind=="hero" && target.Seat.HasValue && state.Players[target.Seat.Value].Cards.Any(c => c.PlayedRound==state.Round && c.PlayedTurn==state.Turn && catalog.Card(c.CardId).PrimaryFamily=="attack"))
                    result.Amount=program.AttackBonusValue;
            }
            else if (program.AttackBonusKind==AttackBonusKind.AdjacentEnemies)
            {
                result.Reason="source_adjacent_enemies";
                result.UnitSources=state.Units.Where(u => u.Team!=source.Team && IsCombatUnit(u) && u.Position.Distance(source.Position)==1).Select(u => u.Id).OrderBy(id => id,System.StringComparer.Ordinal).ToList();
                result.Amount=result.UnitSources.Count*program.AttackBonusValue;
            }
            else if (program.AttackBonusKind==AttackBonusKind.OtherFriendlySupport)
            {
                result.Reason="target_adjacent_other_allies";
                result.UnitSources=state.Units.Where(u => u.Id!=source.Id && u.Team==source.Team && IsCombatUnit(u) && u.Position.Distance(target.Position)==1).Select(u => u.Id).OrderBy(id => id,System.StringComparer.Ordinal).ToList();
                result.Amount=result.UnitSources.Count>0 ? program.AttackBonusValue : 0;
                result.Unblockable=result.UnitSources.Count>0 && program.SupportMakesUnblockable;
            }
            return result;
        }
        private static bool IsCombatUnit(UnitState unit) => unit.Kind=="hero" || unit.Kind=="melee" || unit.Kind=="ranged" || unit.Kind=="heavy";
        internal static int ConditionalRangeBonus(PlayerState player,PrimaryProgram program,bool discarded)
        {
            bool enabled=program.RangeBonusKind==AttackRangeBonusKind.DiscardedBeforeAttack && discarded ||
                program.RangeBonusKind==AttackRangeBonusKind.OwnDiscardPile && player.Cards.Any(c=>c.Zone==CardZone.Discarded);
            return enabled ? program.RangeBonusValue : 0;
        }
        internal static int AttackDistance(ContentCatalog catalog,GameState state,CardDefinition card,PrimaryProgram program,int seat)
        {
            int ultimateRange=card.PrimaryCategory=="基础攻击" ? UltimateRules.BasicAttackRangeBonus(catalog,state,seat) : 0;
            if(program.AdjacentAttack) return 1+ultimateRange;
            var execution=state.Execution;
            int bonus=execution!=null && execution.ControllerSeat==seat && execution.CardId==card.Id && execution.AttackRangeLocked
                ? execution.AttackRangeBonus : ConditionalRangeBonus(state.Players[seat],program,false);
            return (card.SubtypeValue??0)+state.Players[seat].RangedBonus+bonus+ultimateRange;
        }
        public static int? CurrentAttackRange(ContentCatalog catalog,GameState state)
        {
            if(!state.ActiveSeat.HasValue) return null;
            int seat=state.ActiveSeat.Value;
            var instance=state.Players[seat].Cards.SingleOrDefault(c=>c.Zone==CardZone.PlayedUnresolved);
            if(instance==null || !state.Units.Any(u=>u.Seat==seat)) return null;
            var card=catalog.Card(instance.CardId); var program=CardPrograms.Primary(card,state.EngineVersion);
            return program!=null && program.Instructions.Contains(InstructionKind.Attack) ? AttackDistance(catalog,state,card,program,seat) : (int?)null;
        }
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
            return state.Pending?.ResumeAt=="repeat_once_different"
                ? DifferentRepeatTargets(catalog,state,source,card,program)
                : Targets(catalog, state, source, card, program);
        }
        internal static List<string> DifferentRepeatTargets(ContentCatalog catalog,GameState state,UnitState source,CardDefinition card,PrimaryProgram program)
        {
            if(!state.Units.Any(u=>u.Kind=="hero" && u.Team!=source.Team && u.Position.Distance(source.Position)==1)) return new List<string>();
            return Targets(catalog,state,source,card,program).Where(id=>id!=state.Execution!.TargetUnitId).ToList();
        }
        internal static List<string> Targets(ContentCatalog catalog, GameState state, UnitState source, CardDefinition card, PrimaryProgram program)
        {
            int distance = AttackDistance(catalog,state,card,program,source.Seat!.Value);
            var removable = new HashSet<string>(GameRules.LegalMinionRemovals(state));
            return state.Units.Where(u => (state.EngineVersion<58 || state.Execution?.UltimateRepeatExcludedTarget != u.Id) && u.Team != source.Team && u.Position.Distance(source.Position) >= program.MinimumDistance &&
                    u.Position.Distance(source.Position) <= distance && (u.Kind == "hero" || !program.OnlyHeroes && removable.Contains(u.Id)) &&
                    (!program.ExcludeStraightLine || !source.Position.IsInStraightLineWith(u.Position)) &&
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
            int defenseBonus=state.Players[seat].DefenseBonus+EffectRules.DefenseBonus(catalog,state,seat);
            foreach (var instance in state.Players[seat].Cards.Where(c => c.Zone == CardZone.InHand))
            {
                var card = catalog.Card(instance.CardId); var primary = CardPrograms.Defense(card,state.EngineVersion);
                if (card.PrimaryFamily == "defense")
                {
                    if (primary == null) continue;
                    if (DefenseRestriction(state,primary,attack,attacker,defender)!="") continue;
                    result.Add(new DefenseOption { CardId = card.Id, Primary = true, Block = primary.Block, IgnoresMinions = primary.IgnoresMinions,
                        Assessment = CombatMath.Defense(attack, card.PrimaryValue, defenseBonus, primary.IgnoresMinions, primary.Block) });
                }
                else if (card.SecondaryDefense.HasValue)
                    result.Add(new DefenseOption { CardId = card.Id, Assessment = CombatMath.Defense(attack, card.SecondaryDefense.Value, defenseBonus) });
            }
            return result;
        }
        public static Dictionary<string,string> DefenseRestrictions(ContentCatalog catalog,GameState state,int seat)
        {
            var result=new Dictionary<string,string>(); var attack=state.Execution?.Attack;
            if(attack==null || state.Pending?.Kind!="defense" || state.Pending.ChooserSeat!=seat || seat!=attack.DefenderSeat) return result;
            var defender=state.Units.SingleOrDefault(u => u.Seat==seat); var attacker=state.Units.SingleOrDefault(u => u.Seat==attack.AttackerSeat);
            if(defender==null || attacker==null) return result;
            foreach(var instance in state.Players[seat].Cards.Where(c => c.Zone==CardZone.InHand))
            {
                var program=CardPrograms.Defense(catalog.Card(instance.CardId),state.EngineVersion);
                if(program==null) continue;
                string reason=DefenseRestriction(state,program,attack,attacker,defender);
                if(reason!="") result.Add(instance.CardId,reason);
            }
            return result;
        }
        private static string DefenseRestriction(GameState state,DefenseProgram program,AttackBreakdown attack,UnitState attacker,UnitState defender)
        {
            if(program.Block && attack.Unblockable) return "unblockable";
            if(program.AttackKind==DefenseAttackKind.Ranged && !attack.Ranged) return "requires_ranged";
            if(program.AttackKind==DefenseAttackKind.NonRanged && attack.Ranged) return "requires_non_ranged";
            if(attacker.Position.Distance(defender.Position)<program.MinimumDistance) return "requires_minimum_distance";
            if(program.RequiresAdjacentFriendlyMinion && !state.Units.Any(u => u.Team==defender.Team &&
                (u.Kind=="melee" || u.Kind=="ranged" || u.Kind=="heavy") && u.Position.Distance(defender.Position)==1)) return "requires_adjacent_friendly_minion";
            return "";
        }
        public static List<string> UnimplementedDefenses(ContentCatalog catalog, GameState state, int seat)
        {
            if (state.Pending?.Kind != "defense" || state.Pending.ChooserSeat != seat) return new List<string>();
            return state.Players[seat].Cards.Where(c => c.Zone == CardZone.InHand).Select(c => catalog.Card(c.CardId))
                .Where(c => c.PrimaryFamily == "defense" && CardPrograms.Defense(c,state.EngineVersion) == null).Select(c => c.Id).ToList();
        }
    }
}

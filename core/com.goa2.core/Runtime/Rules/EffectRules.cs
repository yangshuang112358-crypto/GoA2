#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public static class EffectRules
    {
        public static Dictionary<string,string> MinionCombatKinds(ContentCatalog catalog,GameState state,CardDefinition attack,int attackerSeat)
        {
            var result=new Dictionary<string,string>();
            if(state.EngineVersion<30 || attack.PrimaryFamily!="attack")return result;
            var team=state.Players[attackerSeat].Team;
            var effects=Current(state,EffectKind.FriendlyBasicMinionsRanged).Where(e=>attack.PrimaryCategory=="基础攻击");
            if(state.EngineVersion>=31)effects=effects.Concat(Current(state,EffectKind.FriendlyAttackMinionsRanged));
            if(state.EngineVersion>=32)effects=effects.Concat(Current(state,EffectKind.FriendlyAttackMinionsDual));
            foreach(var effect in effects)
            {
                var source=Source(state,effect);
                if(source==null || state.Players[effect.ControllerSeat].Team!=team)continue;
                int radius=Radius(catalog,state,effect);
                foreach(var minion in state.Units.Where(u=>u.Team==team && (u.Kind=="melee" || u.Kind=="ranged" || u.Kind=="heavy") && u.Position.Distance(source.Position)<=radius))
                    result[minion.Id]=effect.Kind==EffectKind.FriendlyAttackMinionsDual ? "melee_ranged" : "ranged";
            }
            return result;
        }
        private static IEnumerable<ActiveEffect> Current(GameState state, EffectKind kind) => state.Effects.Where(e => e.Kind == kind && EffectTimeline.Active(e.Window,state.Round,state.Turn));
        private static UnitState? Source(GameState state, ActiveEffect effect) => state.Units.SingleOrDefault(u => u.Id == effect.SourceUnitId);
        private static int Radius(ContentCatalog catalog, GameState state, ActiveEffect effect) => effect.AreaKind==EffectAreaKind.Adjacent ? 1 :
            (catalog.Card(effect.SourceCardId).SubtypeValue ?? 0) + state.Players[effect.ControllerSeat].RangeBonus;
        public static List<Hex> Area(ContentCatalog catalog, GameState state, ActiveEffect effect)
        {
            var source=Source(state,effect);
            if (source == null || effect.AreaKind==EffectAreaKind.None || !EffectTimeline.Active(effect.Window,state.Round,state.Turn)) return new List<Hex>();
            int radius=Radius(catalog,state,effect);
            return catalog.Cells.Where(c => c.Position.Distance(source.Position)<=radius).Select(c => c.Position).ToList();
        }
        public static List<string> CancellableAdjacentSkills(ContentCatalog catalog, GameState state, int controllerSeat)
        {
            var controller=state.Units.SingleOrDefault(u => u.Seat==controllerSeat);
            if (controller==null) return new List<string>();
            var enemies=new HashSet<int>(state.Units.Where(u => u.Kind=="hero" && u.Seat.HasValue && u.Team!=controller.Team && CanAffect(state,controllerSeat,u) && u.Position.Distance(controller.Position)==1).Select(u => u.Seat!.Value));
            return state.Effects.Where(e => enemies.Contains(e.ControllerSeat) && catalog.Card(e.SourceCardId).PrimaryFamily=="skill" && EffectTimeline.Active(e.Window,state.Round,state.Turn))
                .OrderBy(e => e.CreationOrder).Select(e => e.Id).ToList();
        }
        public static string SkillRestriction(ContentCatalog catalog, GameState state, int seat, CardDefinition card)
        {
            if (card.PrimaryFamily != "skill") return "";
            var target = state.Units.SingleOrDefault(u => u.Seat == seat);
            if (target == null) return "";
            foreach (var effect in Current(state,EffectKind.SkillSuppression))
            {
                var source = Source(state,effect);
                if (source != null && CanAffect(state,effect.ControllerSeat,target) && state.Players[effect.ControllerSeat].Team != target.Team && source.Position.Distance(target.Position) <= Radius(catalog,state,effect))
                    return effect.SourceCardId;
            }
            return "";
        }
        public static bool CanAffect(GameState state,int controllerSeat,UnitState target)
        {
            if(state.EngineVersion<51 || target.Seat==controllerSeat)return true;
            if(Current(state,EffectKind.ImmunityAndUnitTraversal).Any(e=>e.SourceUnitId==target.Id))return false;
            if(state.EngineVersion<52 || controllerSeat<0 || controllerSeat>=state.Players.Count || state.Players[controllerSeat].Team==target.Team)return true;
            return !Current(state,EffectKind.OtherEnemyActionImmunity).Any(e=>e.ProtectedUnitId==target.Id && e.ExemptControllerSeat!=controllerSeat);
        }
        public static bool CanTraverseUnits(GameState state,UnitState unit) =>
            state.EngineVersion>=51 && Current(state,EffectKind.ImmunityAndUnitTraversal).Any(e=>e.SourceUnitId==unit.Id);
        public static bool CanDisplace(ContentCatalog catalog,GameState state,int controllerSeat,UnitState target)
        {
            if(!CanAffect(state,controllerSeat,target))return false;
            if(state.EngineVersion<50 || state.Players[controllerSeat].Team==target.Team)return true;
            return !Current(state,EffectKind.FriendlyDisplacementProtection).Any(effect=>
            {
                var source=Source(state,effect);
                return source!=null && source.Team==target.Team && (source.Id==target.Id || source.Position.Distance(target.Position)<=Radius(catalog,state,effect));
            });
        }
        public static bool CanBeAttacked(GameState state, UnitState source, UnitState target, bool ranged)
        {
            if(!CanAffect(state,source.Seat??-1,target))return false;
            if (source.Kind!="hero" || !ranged || source.Position.Distance(target.Position)<=1) return true;
            return !Current(state,EffectKind.NonAdjacentRangedImmunity).Any(e => e.ProtectedUnitId==target.Id);
        }
        public static int DefenseBonus(ContentCatalog catalog,GameState state,int defenderSeat)
        {
            if(state.EngineVersion<47)return 0;
            var defender=state.Units.SingleOrDefault(u=>u.Kind=="hero" && u.Seat==defenderSeat);if(defender==null)return 0;
            if(!state.Units.Any(u=>u.Team==defender.Team && (u.Kind=="melee" || u.Kind=="ranged" || u.Kind=="heavy") && u.Position.Distance(defender.Position)==1))return 0;
            return Current(state,EffectKind.FriendlyNearMinionDefense).Count(effect=>
            {
                var source=Source(state,effect);
                return source!=null && CanAffect(state,effect.ControllerSeat,defender) && state.Players[effect.ControllerSeat].Team==defender.Team &&
                    (source.Id==defender.Id || source.Position.Distance(defender.Position)<=Radius(catalog,state,effect));
            });
        }
        public static bool CanMoveAcross(ContentCatalog catalog, GameState state, UnitState unit, Hex from, Hex to)
        {
            // A card ignoring heavy immunity does not remove immunity against other sources.
            if(state.EngineVersion>=43 && unit.Kind=="heavy" && !GameRules.LegalMinionRemovals(state).Contains(unit.Id))return true;
            foreach (var effect in Current(state,EffectKind.MovementBoundary))
            {
                var source = Source(state,effect);
                if (source == null || !CanAffect(state,effect.ControllerSeat,unit) || state.Players[effect.ControllerSeat].Team == unit.Team) continue;
                int radius = Radius(catalog,state,effect);
                if ((from.Distance(source.Position) <= radius) != (to.Distance(source.Position) <= radius)) return false;
            }
            return true;
        }
    }
}

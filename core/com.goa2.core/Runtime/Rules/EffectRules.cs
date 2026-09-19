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
            var enemies=new HashSet<int>(state.Units.Where(u => u.Kind=="hero" && u.Seat.HasValue && u.Team!=controller.Team && u.Position.Distance(controller.Position)==1).Select(u => u.Seat!.Value));
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
                if (source != null && state.Players[effect.ControllerSeat].Team != target.Team && source.Position.Distance(target.Position) <= Radius(catalog,state,effect))
                    return effect.SourceCardId;
            }
            return "";
        }
        public static bool CanBeAttacked(GameState state, UnitState source, UnitState target, bool ranged)
        {
            if (source.Kind!="hero" || !ranged || source.Position.Distance(target.Position)<=1) return true;
            return !Current(state,EffectKind.NonAdjacentRangedImmunity).Any(e => e.ProtectedUnitId==target.Id);
        }
        public static bool CanMoveAcross(ContentCatalog catalog, GameState state, UnitState unit, Hex from, Hex to)
        {
            // A card ignoring heavy immunity does not remove immunity against other sources.
            if(state.EngineVersion>=43 && unit.Kind=="heavy" && !GameRules.LegalMinionRemovals(state).Contains(unit.Id))return true;
            foreach (var effect in Current(state,EffectKind.MovementBoundary))
            {
                var source = Source(state,effect);
                if (source == null || state.Players[effect.ControllerSeat].Team == unit.Team) continue;
                int radius = Radius(catalog,state,effect);
                if ((from.Distance(source.Position) <= radius) != (to.Distance(source.Position) <= radius)) return false;
            }
            return true;
        }
    }
}

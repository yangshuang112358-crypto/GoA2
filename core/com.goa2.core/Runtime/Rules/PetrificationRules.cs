#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
namespace Goa2.Rules
{
    public static class PetrificationRules
    {
        public static HashSet<string> Targets(GameState state)
        {
            var result=new HashSet<string>();if(state.EngineVersion<95)return result;
            foreach(var effect in state.Effects.Where(e=>(e.Kind==EffectKind.PetrifyNearestEnemyHeroes || state.EngineVersion>=97 && e.Kind==EffectKind.PetrifyAllEnemyHeroes) && EffectTimeline.Active(e.Window,state.Round,state.Turn)))
            {
                var source=state.Units.SingleOrDefault(u=>u.Id==effect.SourceUnitId);if(source==null)continue;
                int radius=effect.BaseRadius+state.Players[effect.ControllerSeat].RangeBonus;
                var enemies=state.Units.Where(u=>u.Kind=="hero" && u.Team!=source.Team && u.Position.Distance(source.Position)<=radius).ToList();
                if(enemies.Count==0)continue;
                // D-061: an already immune nearest hero still occupies the nearest slot.
                int nearest=enemies.Min(u=>u.Position.Distance(source.Position));
                foreach(var unit in enemies.Where(u=>(effect.Kind==EffectKind.PetrifyAllEnemyHeroes || u.Position.Distance(source.Position)==nearest) && EffectRules.CanAffectWithoutPetrification(state,effect.ControllerSeat,u)))result.Add(unit.Id);
            }
            return result;
        }
        public static bool IsPetrified(GameState state,UnitState unit) => unit.Kind=="hero" && Targets(state).Contains(unit.Id);
        public static HashSet<Hex> TerrainCells(GameState state)
        {var ids=Targets(state);return state.Units.Where(u=>ids.Contains(u.Id)).Select(u=>u.Position).ToHashSet();}
    }
}

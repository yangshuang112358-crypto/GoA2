#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
namespace Goa2.Rules
{
    public static class PassiveRules
    {
        public static PoisonMarker? Poison(GameState state,int seat) => state.EngineVersion<91 ? null : state.PoisonMarkers?.SingleOrDefault(m=>m.TargetSeat==seat && m.AppliedRound==state.Round);
        public static int Attack(GameState state,int seat) => Poison(state,seat)==null ? state.Players[seat].AttackBonus : -state.Players[seat].AttackBonus;
        public static int Initiative(GameState state,int seat) => Poison(state,seat)==null ? state.Players[seat].InitiativeBonus : -state.Players[seat].InitiativeBonus;
        public static int Defense(GameState state,int seat) => Poison(state,seat)?.IncludesDefense==true ? -state.Players[seat].DefenseBonus : state.Players[seat].DefenseBonus;
        public static Dictionary<string,int> Effective(GameState state,int seat)
        {
            var p=state.Players[seat];return new Dictionary<string,int>{{"攻击",Attack(state,seat)},{"防御",Defense(state,seat)},{"先攻",Initiative(state,seat)},{"移动",p.MovementBonus},{"范围",p.RangeBonus},{"远程",p.RangedBonus}}.Where(v=>v.Value!=0).ToDictionary(v=>v.Key,v=>v.Value);
        }
    }
    public sealed partial class GameRules
    {
        private static void ApplyPoison(GameState state,Command command,CardExecution execution,bool defense)
        {
            var target=state.Units.SingleOrDefault(u=>u.Id==execution.TargetUnitId && u.Kind=="hero" && u.Seat.HasValue);
            if(target==null || !EffectRules.CanAffect(state,execution.ControllerSeat,target))return;
            if(PassiveRules.Poison(state,target.Seat!.Value)!=null)
            {Emit(state,command,"PoisonUnchanged",target.Seat,execution.CardId,detail:"already_poisoned");return;}
            if(state.PoisonMarkers==null)state.PoisonMarkers=new List<PoisonMarker>();
            state.PoisonMarkers.Add(new PoisonMarker{TargetSeat=target.Seat!.Value,SourceSeat=execution.ControllerSeat,SourceCardId=execution.CardId,AppliedRound=state.Round,IncludesDefense=defense});
            Emit(state,command,"PoisonApplied",target.Seat,execution.CardId,detail:defense?"initiative_attack_defense":"initiative_attack");
        }
        private static void ClearRoundPoison(GameState state,Command command)
        {
            if(state.EngineVersion<91 || state.PoisonMarkers==null)return;
            foreach(var marker in state.PoisonMarkers.OrderBy(m=>m.TargetSeat))Emit(state,command,"PoisonExpired",marker.TargetSeat,marker.SourceCardId,detail:"round_end");
            state.PoisonMarkers=null;
        }
    }
}

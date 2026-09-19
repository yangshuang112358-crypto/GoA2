#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static void RecordDisplacedMinion(GameState state,UnitState unit)
        {
            if(state.EngineVersion<42 || state.Execution==null || !IsMinion(unit))return;
            if(state.Execution.DisplacedMinions==null)state.Execution.DisplacedMinions=new List<string>();
            if(!state.Execution.DisplacedMinions.Contains(unit.Id))state.Execution.DisplacedMinions.Add(unit.Id);
        }
        private static List<UnitState> ReturningMinions(ContentCatalog catalog,GameState state)
        {
            if(state.EngineVersion<42 || state.Execution?.DisplacedMinions==null)return new List<UnitState>();
            return state.Units.Where(u=>IsMinion(u) && state.Execution.DisplacedMinions.Contains(u.Id) && catalog.Cell(u.Position)?.Region!=state.CombatRegion)
                .OrderBy(u=>u.Id,System.StringComparer.Ordinal).ToList();
        }
        private static List<MinionReturnOption> ReturnOptions(ContentCatalog catalog,GameState state,UnitState unit)
        {
            var occupied=new HashSet<Hex>(state.Units.Where(u=>u.Id!=unit.Id).Select(u=>u.Position));
            var cells=catalog.Cells.Where(c=>!c.Obstacle && !occupied.Contains(c.Position)).ToDictionary(c=>c.Position);
            var goals=cells.Values.Where(c=>c.Region==state.CombatRegion).Select(c=>c.Position).ToList();
            if(goals.Count==0)return new List<MinionReturnOption>();
            var distances=goals.ToDictionary(p=>p,p=>0);var queue=new Queue<Hex>(goals);
            // Reverse BFS preserves every equal shortest route without enumerating exponentially many paths.
            while(queue.Count>0)
            {
                var current=queue.Dequeue();
                foreach(var previous in current.Neighbors())
                {
                    if(!cells.ContainsKey(previous) || distances.ContainsKey(previous) || !EffectRules.CanMoveAcross(catalog,state,unit,previous,current))continue;
                    distances[previous]=distances[current]+1;queue.Enqueue(previous);
                }
            }
            if(distances.TryGetValue(unit.Position,out int remaining) && remaining>0)
                return unit.Position.Neighbors().Where(p=>distances.TryGetValue(p,out int distance) && distance==remaining-1 && EffectRules.CanMoveAcross(catalog,state,unit,unit.Position,p))
                    .OrderBy(p=>p.X).ThenBy(p=>p.Y).Select(p=>new MinionReturnOption{UnitId=unit.Id,Destination=p,RemainingDistance=remaining-1}).ToList();
            int nearest=goals.Min(p=>p.Distance(unit.Position));
            return goals.Where(p=>p.Distance(unit.Position)==nearest).OrderBy(p=>p.X).ThenBy(p=>p.Y)
                .Select(p=>new MinionReturnOption{UnitId=unit.Id,Destination=p,Place=true}).ToList();
        }
        private static List<UnitState> CurrentReturnGroup(ContentCatalog catalog,GameState state)
        {
            var units=ReturningMinions(catalog,state);
            if(units.Count==0)return units;
            var locked=units.SingleOrDefault(u=>u.Id==state.Execution!.ReturningMinionId);
            if(locked!=null)return new List<UnitState>{locked};
            var team=units.Any(u=>u.Team==state.DecisionCoin)?state.DecisionCoin:OtherTeam(state.DecisionCoin);
            return units.Where(u=>u.Team==team).ToList();
        }
        public static List<MinionReturnOption> LegalMinionReturns(ContentCatalog catalog,GameState state,int seat)
        {
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="minion_return" || state.Pending.ChooserSeat!=seat)return new List<MinionReturnOption>();
            var units=CurrentReturnGroup(catalog,state);
            if(units.Count==0 || (units[0].Team==Team.Blue?state.BlueCaptain:state.RedCaptain)!=seat)return new List<MinionReturnOption>();
            return units.SelectMany(u=>ReturnOptions(catalog,state,u)).ToList();
        }
        private static bool BeginMinionReturns(ContentCatalog catalog,GameState state,Command command)
        {
            var execution=state.Execution;
            if(execution==null || state.EngineVersion<42 || execution.DisplacedMinions==null)return false;
            var units=CurrentReturnGroup(catalog,state);
            if(units.Count==0){execution.DisplacedMinions=null;execution.ReturningMinionId=null;return false;}
            int captain=units[0].Team==Team.Blue?state.BlueCaptain:state.RedCaptain;
            var options=units.SelectMany(u=>ReturnOptions(catalog,state,u)).ToList();
            Require(options.Count>0,"minion_return_unavailable","战区没有可供小兵回归的空格。");
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice
            {
                Id="minion-return:"+(state.Events.Count+1),Kind="minion_return",ChooserSeat=captain,
                Source=execution.CardId,ResumeAt="finish_card_after_minion_return",UnitId=execution.ReturningMinionId??"",
                CandidateUnits=units.Select(u=>u.Id).ToList(),CandidateCells=options.Select(o=>o.Destination).Distinct().ToList()
            };
            Emit(state,command,"MinionReturnChoiceRequired",captain,execution.CardId);
            return true;
        }
        private static void ChooseMinionReturn(ContentCatalog catalog,GameState state,Command command)
        {
            Require(command.MoveMode==MoveMode.Secondary,"invalid_minion_return","小兵回归不能替换成快速移动。");
            var option=LegalMinionReturns(catalog,state,command.ActorSeat).SingleOrDefault(o=>o.UnitId==command.Value && o.Destination==command.Destination);
            Require(option!=null,"invalid_minion_return","请由对应队长选择当前小兵最短回归路线或就近放置格。");
            var unit=state.Units.Single(u=>u.Id==option!.UnitId);var origin=unit.Position;var execution=state.Execution!;
            unit.Position=option!.Destination;execution.ReturningMinionId=unit.Id;
            Emit(state,command,option.Place?"MinionReturnPlaced":"MinionReturnMoved",command.ActorSeat,execution.CardId,detail:unit.Id);
            var e=state.Events.Last();e.From=origin;e.To=unit.Position;if(!option.Place)e.Path=new List<Hex>{origin,unit.Position};
            if(catalog.Cell(unit.Position)!.Region==state.CombatRegion)
            {
                execution.DisplacedMinions!.Remove(unit.Id);execution.ReturningMinionId=null;
                Emit(state,command,"MinionReturnCompleted",command.ActorSeat,execution.CardId,detail:unit.Id);
            }
            state.Pending=null;state.Phase=Phase.Action;
            EndCardExecution(catalog,state,command);
        }
    }
}

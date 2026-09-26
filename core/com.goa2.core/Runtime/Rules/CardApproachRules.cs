#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static PrimaryProgram? ApproachProgram(ContentCatalog catalog,GameState state)
        {
            var e=state.Execution;if(state.EngineVersion<67 || e==null)return null;
            var p=CardPrograms.Primary(catalog.Card(e.CardId),state.EngineVersion);
            return p!=null && p.Id==e.ProgramId && p.Version==e.ProgramVersion && e.Cursor>=0 && e.Cursor<p.Instructions.Count &&
                (p.Instructions[e.Cursor]==InstructionKind.ChooseNearestApproachTarget || p.Instructions[e.Cursor]==InstructionKind.OptionalApproachMove ||
                 p.Instructions[e.Cursor]==InstructionKind.OptionalRepeatApproach) ? p : null;
        }
        private static List<string> ApproachTargets(ContentCatalog catalog,GameState state)
        {
            if(ApproachProgram(catalog,state)==null)return new List<string>();var e=state.Execution!;
            var source=state.Units.SingleOrDefault(u=>u.Seat==e.ControllerSeat);if(source==null)return new List<string>();
            int range=(catalog.Card(e.CardId).SubtypeValue??0)+state.Players[e.ControllerSeat].RangedBonus;
            var minions=new HashSet<string>(LegalMinionRemovals(state));
            var targets=state.Units.Where(u=>u.Team!=source.Team && (u.Kind=="hero" || IsMinion(u) && minions.Contains(u.Id)) &&
                u.Position.Distance(source.Position)>1 && u.Position.Distance(source.Position)<=range && EffectRules.CanDisplace(catalog,state,e.ControllerSeat,u)).ToList();
            if(targets.Count==0)return new List<string>();int nearest=targets.Min(u=>u.Position.Distance(source.Position));
            return targets.Where(u=>u.Position.Distance(source.Position)==nearest).Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
        }
        private static List<string> LegalApproachTargets(ContentCatalog catalog,GameState state,int seat)
        {
            var p=ApproachProgram(catalog,state);var e=state.Execution;
            if(p==null || e==null || state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_target" || state.Pending.ChooserSeat!=seat ||
                state.ActiveSeat!=seat || e.ControllerSeat!=seat || (state.Pending.ResumeAt!="approach_target" && state.Pending.ResumeAt!="approach_repeat"))return new List<string>();
            return ApproachTargets(catalog,state);
        }
        private static bool BeginApproachTarget(ContentCatalog catalog,GameState state,Command command,bool repeat)
        {
            var e=state.Execution!;if(repeat && e.ApproachRepeated)return false;
            var targets=ApproachTargets(catalog,state);if(targets.Count==0)return false;
            state.Phase=Phase.EffectChoice;state.Pending=new PendingChoice{Id="approach-target:"+(state.Events.Count+1),Kind="effect_target",ChooserSeat=e.ControllerSeat,
                Source=e.CardId,ResumeAt=repeat?"approach_repeat":"approach_target",Optional=repeat,CandidateUnits=targets};
            Emit(state,command,repeat?"ActionRepeatChoiceRequired":"EffectTargetChoiceRequired",e.ControllerSeat,e.CardId);return true;
        }
        private static void ChooseApproachTarget(ContentCatalog catalog,GameState state,Command command,bool allowBeforeAction)
        {
            var p=ApproachProgram(catalog,state);var e=state.Execution;
            bool repeat=state.Pending?.ResumeAt=="approach_repeat";
            Require(p!=null && e!=null && state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_target" && state.Pending.ChooserSeat==command.ActorSeat &&
                e.ControllerSeat==command.ActorSeat && state.ActiveSeat==command.ActorSeat && (!repeat || !e.ApproachRepeated) &&
                (repeat && command.Value=="skip" || LegalApproachTargets(catalog,state,command.ActorSeat).Contains(command.Value)),"invalid_effect_target","请选择最近的合法非相邻敌方单位；只有重复可以跳过。");
            if(repeat && command.Value=="skip")
            {e!.Cursor++;Emit(state,command,"ActionRepeatSkipped",command.ActorSeat,e.CardId);}
            else
            {
                if(repeat)
                {
                    if(allowBeforeAction)
                    {
                        e!.ActionInstanceId=NewActionInstance(state);
                        if(BeginBeforeAction(catalog,state,command))return;
                    }
                    e!.ApproachRepeated=true;e.Cursor=1;
                    Emit(state,command,"ActionRepeated",command.ActorSeat,e.CardId,detail:command.Value);
                }
                else e!.Cursor++;
                e!.TargetUnitId=command.Value;Emit(state,command,"EffectTargetChosen",command.ActorSeat,e.CardId,detail:command.Value);
            }
            state.Pending=null;state.Phase=Phase.Action;ContinueCard(catalog,state,command);
        }
        private static List<MoveOption> ApproachMoves(ContentCatalog catalog,GameState state)
        {
            var program=ApproachProgram(catalog,state);var e=state.Execution;
            if(program==null || e==null)return new List<MoveOption>();
            var source=state.Units.SingleOrDefault(u=>u.Seat==e.ControllerSeat);var unit=state.Units.SingleOrDefault(u=>u.Id==e.TargetUnitId);
            if(source==null || unit==null || !EffectRules.CanDisplace(catalog,state,e.ControllerSeat,unit) || IsMinion(unit) && !LegalMinionRemovals(state).Contains(unit.Id))return new List<MoveOption>();
            var occupied=new HashSet<Hex>(state.Units.Where(u=>u.Id!=unit.Id).Select(u=>u.Position));
            bool traverseTerrain=UltimateRules.CanTraverseObstacles(catalog,state,unit),traverseUnits=traverseTerrain || EffectRules.CanTraverseUnits(state,unit);
            var cells=catalog.Cells.ToDictionary(c=>c.Position);
            bool Enter(Hex p)=>cells.TryGetValue(p,out var c) && (!c.Obstacle || traverseTerrain) && (!occupied.Contains(p) || traverseUnits);
            bool Stop(Hex p)=>cells.TryGetValue(p,out var c) && !c.Obstacle && !occupied.Contains(p);
            var goals=source.Position.Neighbors().Where(Stop).ToList();
            var distances=goals.ToDictionary(p=>p,p=>0);var queue=new Queue<Hex>(goals);
            // Reverse BFS measures valid paths, not geometric closeness of each individual step.
            while(queue.Count>0)
            {
                var current=queue.Dequeue();
                foreach(var previous in current.Neighbors())
                {
                    if(distances.ContainsKey(previous) || !Enter(previous) || !EffectRules.CanMoveAcross(catalog,state,unit,previous,current))continue;
                    distances[previous]=distances[current]+1;queue.Enqueue(previous);
                }
            }
            if(!distances.ContainsKey(unit.Position))return new List<MoveOption>();
            var paths=new Dictionary<Hex,List<Hex>>{{unit.Position,new List<Hex>{unit.Position}}};queue.Enqueue(unit.Position);var result=new List<MoveOption>();
            while(queue.Count>0)
            {
                var current=queue.Dequeue();if(paths[current].Count-1>=program.TextMoveDistance)continue;
                foreach(var next in current.Neighbors())
                {
                    if(paths.ContainsKey(next) || !distances.TryGetValue(next,out int remaining) || remaining!=distances[current]-1 || !EffectRules.CanMoveAcross(catalog,state,unit,current,next))continue;
                    var path=new List<Hex>(paths[current]){next};paths.Add(next,path);queue.Enqueue(next);
                    if(Stop(next))result.Add(new MoveOption{Destination=next,Path=path});
                }
            }
            return result.OrderBy(m=>m.Destination.X).ThenBy(m=>m.Destination.Y).ToList();
        }
        private static bool BeginApproachMove(ContentCatalog catalog,GameState state,Command command)
        {
            var moves=ApproachMoves(catalog,state);var e=state.Execution!;
            if(moves.Count==0){Emit(state,command,"EffectMoveSkipped",e.ControllerSeat,e.CardId,detail:"no_valid_path");return false;}
            state.Phase=Phase.EffectChoice;state.Pending=new PendingChoice{Id="approach-move:"+(state.Events.Count+1),Kind="effect_move",ChooserSeat=e.ControllerSeat,
                Source=e.CardId,UnitId=e.TargetUnitId,ResumeAt="approach_move",Optional=true,CandidateCells=moves.Select(m=>m.Destination).ToList()};
            Emit(state,command,"EffectMoveChoiceRequired",e.ControllerSeat,e.CardId,detail:ApproachProgram(catalog,state)!.TextMoveDistance.ToString());return true;
        }
        private static List<MoveOption> LegalApproachMoves(ContentCatalog catalog,GameState state,int seat) =>
            state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_move" && state.Pending.ResumeAt=="approach_move" && state.Pending.ChooserSeat==seat &&
            state.ActiveSeat==seat && state.Execution?.ControllerSeat==seat ? ApproachMoves(catalog,state) : new List<MoveOption>();
        private static void ChooseApproachMove(ContentCatalog catalog,GameState state,Command command)
        {
            Require(ApproachProgram(catalog,state)!=null && state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_move" && state.Pending.ResumeAt=="approach_move" &&
                state.Pending.ChooserSeat==command.ActorSeat && state.Execution?.ControllerSeat==command.ActorSeat && command.MoveMode==MoveMode.Secondary &&
                (command.Value=="" || command.Value=="skip"),"invalid_effect_move","请由技能操作者选择最短有效路径上的落点或不移动。");
            var e=state.Execution!;
            if(command.Value=="skip")Emit(state,command,"EffectMoveSkipped",command.ActorSeat,e.CardId,detail:"declined");
            else
            {
                var move=LegalApproachMoves(catalog,state,command.ActorSeat).SingleOrDefault(m=>m.Destination==command.Destination);
                Require(move!=null,"invalid_effect_move","该格不在最多2步的最短有效接近路径上。");
                var unit=state.Units.Single(u=>u.Id==e.TargetUnitId);var origin=unit.Position;unit.Position=command.Destination;RecordDisplacedMinion(state,unit);
                Emit(state,command,"UnitMoved",unit.Seat,e.CardId,detail:"by:"+command.ActorSeat+"|unit:"+unit.Id);
                var moved=state.Events.Last();moved.From=origin;moved.To=unit.Position;moved.Path=move!.Path;
            }
            e.Cursor++;state.Pending=null;state.Phase=Phase.Action;ContinueCard(catalog,state,command);
        }
    }
}

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static PrimaryProgram? UnitPushProgram(ContentCatalog catalog,GameState state)
        {
            var e=state.Execution;if(e==null)return null;var p=CardPrograms.Primary(catalog.Card(e.CardId),state.EngineVersion);
            return p!=null && p.Id==e.ProgramId && p.Version==e.ProgramVersion && e.Cursor>=0 && e.Cursor<p.Instructions.Count && p.Instructions[e.Cursor]==InstructionKind.ChooseUnitPush?p:null;
        }
        private static List<string> UnitPushTargets(ContentCatalog catalog,GameState state)
        {
            var p=UnitPushProgram(catalog,state);var e=state.Execution;if(p==null || e==null)return new List<string>();
            var source=state.Units.SingleOrDefault(u=>u.Seat==e.ControllerSeat);if(source==null)return new List<string>();
            bool attack=catalog.Card(e.CardId).PrimaryFamily=="attack";var minions=new HashSet<string>(LegalMinionRemovals(state));
            return state.Units.Where(u=>u.Team!=source.Team && u.Position.Distance(source.Position)==1 &&
                (minions.Contains(u.Id) || p.PushTarget==PushTargetKind.EnemyUnit && u.Kind=="hero") &&
                EffectRules.CanDisplace(catalog,state,e.ControllerSeat,u,attackAction:attack)).Select(u=>u.Id).OrderBy(id=>id,System.StringComparer.Ordinal).ToList();
        }
        private static List<string> LegalUnitPushTargets(ContentCatalog catalog,GameState state,int seat)=>
            state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_target" && state.Pending.ResumeAt=="single_push_target" && state.Pending.ChooserSeat==seat &&
            state.ActiveSeat==seat && state.Execution?.ControllerSeat==seat ? UnitPushTargets(catalog,state) : new List<string>();
        private static bool BeginUnitPush(ContentCatalog catalog,GameState state,Command command)
        {
            var targets=UnitPushTargets(catalog,state);if(targets.Count==0)return false;var e=state.Execution!;var p=UnitPushProgram(catalog,state)!;
            state.Phase=Phase.EffectChoice;state.Pending=new PendingChoice{Id="unit-push:"+(state.Events.Count+1),Kind="effect_target",ChooserSeat=e.ControllerSeat,
                Source=e.CardId,ResumeAt="single_push_target",Optional=p.OptionalPushTarget,CandidateUnits=targets};
            Emit(state,command,"EffectTargetChoiceRequired",e.ControllerSeat,e.CardId,detail:"single_push_target");return true;
        }
        private static PushResult? CurrentUnitPush(ContentCatalog catalog,GameState state)
        {
            var p=UnitPushProgram(catalog,state);var e=state.Execution;if(p==null || e==null || state.Pending?.UnitId==null || !UnitPushTargets(catalog,state).Contains(state.Pending.UnitId))return null;
            return PushRules.AwayFromAdjacent(catalog,state,state.Units.Single(u=>u.Seat==e.ControllerSeat),state.Units.Single(u=>u.Id==state.Pending.UnitId),p.TextPushDistance);
        }
        private static List<MoveOption> LegalUnitPushMoves(ContentCatalog catalog,GameState state,int seat)
        {
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_move" || state.Pending.ResumeAt!="single_push_distance" || state.Pending.ChooserSeat!=seat ||
                state.Execution?.ControllerSeat!=seat || state.ActiveSeat!=seat)return new List<MoveOption>();
            var result=CurrentUnitPush(catalog,state);return result==null?new List<MoveOption>():result.Path.Skip(1).Select((cell,index)=>new MoveOption{Destination=cell,Path=result.Path.Take(index+2).ToList()})
                .Where(m=>catalog.Cell(m.Destination)?.Obstacle==false && !state.Units.Any(u=>u.Position==m.Destination)).ToList();
        }
        private static void ChooseUnitPushTarget(ContentCatalog catalog,GameState state,Command command)
        {
            var p=UnitPushProgram(catalog,state);var e=state.Execution;
            Require(p!=null && e!=null && state.Phase==Phase.EffectChoice && state.Pending?.ResumeAt=="single_push_target" && state.Pending.ChooserSeat==command.ActorSeat &&
                (command.Value=="skip" && p.OptionalPushTarget || LegalUnitPushTargets(catalog,state,command.ActorSeat).Contains(command.Value)),"invalid_effect_target","请选择相邻且可以推动的敌方单位。");
            if(command.Value=="skip")
            {e!.Cursor++;state.Pending=null;state.Phase=Phase.Action;Emit(state,command,"PushSkipped",command.ActorSeat,e.CardId,detail:"no_target_selected");ContinueCard(catalog,state,command);return;}
            Emit(state,command,"EffectTargetChosen",command.ActorSeat,e!.CardId,detail:command.Value);
            state.Pending=new PendingChoice{Id="unit-push-distance:"+(state.Events.Count+1),Kind="effect_move",ChooserSeat=e.ControllerSeat,Source=e.CardId,
                UnitId=command.Value,ResumeAt="single_push_distance",Optional=true};
            var result=CurrentUnitPush(catalog,state)!;
            if(result.Path.Count==1 || p!.FixedPushDistance){CompleteUnitPush(catalog,state,command,result.Path,result.StopReason);return;}
            state.Pending.CandidateCells=LegalUnitPushMoves(catalog,state,e.ControllerSeat).Select(m=>m.Destination).ToList();Emit(state,command,"EffectMoveChoiceRequired",e.ControllerSeat,e.CardId,detail:"push_up_to:"+p!.TextPushDistance);
        }
        private static void ChooseUnitPushDistance(ContentCatalog catalog,GameState state,Command command)
        {
            Require(UnitPushProgram(catalog,state)!=null && state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_move" && state.Pending.ResumeAt=="single_push_distance" &&
                state.Pending.ChooserSeat==command.ActorSeat && state.Execution?.ControllerSeat==command.ActorSeat && command.MoveMode==MoveMode.Secondary && (command.Value=="" || command.Value=="skip"),
                "invalid_effect_move","请由推动者选择合法推动落点或不推动。");
            if(command.Value=="skip")
            {var e=state.Execution!;e.Cursor++;state.Pending=null;state.Phase=Phase.Action;Emit(state,command,"PushSkipped",command.ActorSeat,e.CardId,detail:"declined");ContinueCard(catalog,state,command);return;}
            var move=LegalUnitPushMoves(catalog,state,command.ActorSeat).SingleOrDefault(m=>m.Destination==command.Destination);
            Require(move!=null,"invalid_effect_move","该格不在允许的推动直线上。");var result=CurrentUnitPush(catalog,state)!;
            CompleteUnitPush(catalog,state,command,move!.Path,move.Path.Count==result.Path.Count?result.StopReason:"");
        }
        private static void CompleteUnitPush(ContentCatalog catalog,GameState state,Command command,List<Hex> path,string stopReason)
        {
            var e=state.Execution!;var target=state.Units.Single(u=>u.Id==state.Pending!.UnitId);target.Position=path.Last();
            if(path.Count>1)RecordDisplacedMinion(state,target);
            Emit(state,command,"UnitPushed",target.Seat,e.CardId,detail:"by:"+e.ControllerSeat+"|unit:"+target.Id);
            var pushed=state.Events.Last();pushed.From=path.First();pushed.To=target.Position;pushed.Path=path;
            if(stopReason!="")Emit(state,command,"PushStopped",target.Seat,e.CardId,detail:stopReason);
            e.Cursor++;state.Pending=null;state.Phase=Phase.Action;AfterHeroPushed(catalog,state,command,e,target);
            if(state.Phase!=Phase.Finished && state.Pending==null)ContinueCard(catalog,state,command);
        }
    }
}

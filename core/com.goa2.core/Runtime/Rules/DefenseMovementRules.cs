#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static DefenseProgram? DefenseMoveProgram(ContentCatalog catalog,GameState state,int seat)
        {
            var response=state.Execution?.DefenseResponse;
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_move" || state.Pending.ResumeAt!="defense_response_move" ||
                state.Pending.ChooserSeat!=seat || response==null || response.ControllerSeat!=seat || response.Cursor!=0)return null;
            var program=CardPrograms.Defense(catalog.Card(response.SourceCardId),state.EngineVersion);
            return program!=null && program.Id==response.ProgramId && program.Version==response.ProgramVersion && program.Followup==DefenseFollowup.OptionalStraightMove ? program : null;
        }
        private static List<MoveOption> LegalDefenseMoves(ContentCatalog catalog,GameState state,int seat)
        {
            var program=DefenseMoveProgram(catalog,state,seat);var response=state.Execution?.DefenseResponse;
            var source=response==null ? null : state.Units.SingleOrDefault(u=>u.Id==response.SourceUnitId && u.Seat==seat);
            return program==null || source==null ? new List<MoveOption>() : MovementRules.StraightExact(catalog,state,source,program.TextMoveDistance);
        }
        private static bool BeginDefenseMove(ContentCatalog catalog,GameState state,Command command,DefenseResponse response,DefenseProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Id==response.SourceUnitId && u.Seat==response.ControllerSeat);
            var options=source==null ? new List<MoveOption>() : MovementRules.StraightExact(catalog,state,source,program.TextMoveDistance);
            if(options.Count==0)
            {
                Emit(state,command,"EffectMoveSkipped",response.ControllerSeat,response.SourceCardId,response.ControllerSeat,source==null?"source_absent":"no_destinations");return false;
            }
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice{Id="defense-move:"+(state.Events.Count+1),Kind="effect_move",ChooserSeat=response.ControllerSeat,
                Source=response.SourceCardId,SourcePrivateTo=response.ControllerSeat,UnitId=response.SourceUnitId,ResumeAt="defense_response_move",Optional=true,CandidateCells=options.Select(o=>o.Destination).ToList()};
            Emit(state,command,"EffectMoveChoiceRequired",response.ControllerSeat,response.SourceCardId,response.ControllerSeat,program.TextMoveDistance.ToString());return true;
        }
        private static void ChooseDefenseMove(ContentCatalog catalog,GameState state,Command command)
        {
            Require(DefenseMoveProgram(catalog,state,command.ActorSeat)!=null && state.Pending!.Optional && command.MoveMode==MoveMode.Secondary &&
                (command.Value=="" || command.Value=="skip"),"invalid_defense_move","请由防御者本人选择完整直线路线或不移动，不能改用快速移动。");
            var response=state.Execution!.DefenseResponse!;
            if(command.Value=="skip")Emit(state,command,"EffectMoveSkipped",response.ControllerSeat,response.SourceCardId,response.ControllerSeat,"declined");
            else
            {
                var option=LegalDefenseMoves(catalog,state,command.ActorSeat).SingleOrDefault(o=>o.Destination==command.Destination);
                Require(option!=null,"invalid_defense_move","该格不能通过完整直线路线到达。");
                var source=state.Units.Single(u=>u.Id==response.SourceUnitId);var origin=source.Position;source.Position=command.Destination;
                // Movement is public; the hand defense card's identity keeps its existing private scope.
                Emit(state,command,"UnitMoved",response.ControllerSeat,detail:"DefenseResponse");var moved=state.Events.Last();moved.From=origin;moved.To=source.Position;moved.Path=option!.Path;
                Emit(state,command,"DefenseMoveResolved",response.ControllerSeat,response.SourceCardId,response.ControllerSeat,"moved");
            }
            response.Cursor++;state.Pending=null;state.Phase=Phase.Action;EndCardExecution(catalog,state,command);
        }
    }
}

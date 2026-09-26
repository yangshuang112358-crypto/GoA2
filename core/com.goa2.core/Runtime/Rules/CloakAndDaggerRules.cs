#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private const string BeforeMoveResume="before_action_move", MovementDestinationResume="before_action_movement_destination",
            UltimateRepeatResume="ultimate_attack_repeat";

        public static bool CanBeginMovementPrelude(ContentCatalog catalog,GameState state,int seat,MoveMode mode)
        {
            if(state.Phase!=Phase.Action || state.Pending!=null || state.Execution!=null || state.BeforeAction!=null || state.ActiveSeat!=seat ||
                !UltimateRules.HasImmuneActionPrelude(catalog,state,seat))return false;
            var played=state.Players[seat].Cards.SingleOrDefault(c=>c.Zone==CardZone.PlayedUnresolved);
            if(played==null)return false;
            var card=catalog.Card(played.CardId);
            bool secondary=card.PrimaryFamily!="movement" && card.SecondaryMovement>0;
            return mode==MoveMode.Secondary ? secondary : mode==MoveMode.Fast && (secondary || card.PrimaryFamily=="movement" && card.PrimaryValue>0);
        }

        private static bool BeginCloakPrelude(ContentCatalog catalog,GameState state,Command command)
        {
            Require(state.BeforeAction==null,"pending_before_action","请先完成当前行动前选择。");
            var program=UltimateRules.OwnedProgram(catalog,state,command.ActorSeat)!;
            var frame=new BeforeActionFrame
            {
                Id="before-action:"+(state.Events.Count+1),SourceCardId=state.Players[command.ActorSeat].PurpleCardId!,
                ProgramId=program.Id,ProgramVersion=program.Version,ControllerSeat=command.ActorSeat,Stage="move",
                ResumeKind=command.Kind,ResumeValue=command.Value,ResumeDestination=command.Destination,ResumeMoveMode=command.MoveMode,
                ParentPhase=state.Phase,ParentActiveSeat=state.ActiveSeat,ParentPending=state.Pending,ParentExecution=state.Execution
            };
            state.BeforeAction=frame;state.Execution=null;state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice {Id=frame.Id+":move",Kind="effect_move",ChooserSeat=frame.ControllerSeat,
                Source=frame.SourceCardId,UnitId="hero:"+frame.ControllerSeat,ResumeAt=BeforeMoveResume,Optional=true};
            state.Pending.CandidateCells=LegalCloakMoves(catalog,state,frame.ControllerSeat).Select(m=>m.Destination).ToList();
            Emit(state,command,"UltimateTriggered",frame.ControllerSeat,frame.SourceCardId,detail:"before:"+command.Kind);
            Emit(state,command,"EffectMoveChoiceRequired",frame.ControllerSeat,frame.SourceCardId,detail:"2");return true;
        }

        private static bool IsCloakMovementWindow(GameState state) => state.EngineVersion>=58 &&
            (state.Pending?.ResumeAt==BeforeMoveResume || state.Pending?.ResumeAt==MovementDestinationResume);

        private static List<MoveOption> LegalCloakMoves(ContentCatalog catalog,GameState state,int seat)
        {
            if(!IsCloakMovementWindow(state) || state.Phase!=Phase.EffectChoice || state.BeforeAction?.ControllerSeat!=seat ||
                state.Pending?.ChooserSeat!=seat || state.Pending.Kind!="effect_move")return new List<MoveOption>();
            var unit=state.Units.SingleOrDefault(u=>u.Kind=="hero" && u.Seat==seat);
            if(unit==null)return new List<MoveOption>();
            return state.BeforeAction.Stage=="move" ? MovementRules.Reachable(catalog,state,unit,2) :
                state.BeforeAction.Stage=="destination" ? MovementRules.ActionMoves(catalog,state,seat,state.BeforeAction.ResumeMoveMode) : new List<MoveOption>();
        }

        private static void BeginMovementDestination(ContentCatalog catalog,GameState state,Command command,BeforeActionFrame frame)
        {
            frame.Stage="destination";state.BeforeAction=frame;state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice {Id=frame.Id+":destination",Kind="effect_move",ChooserSeat=frame.ControllerSeat,
                Source=ActiveCard(state).CardId,UnitId="hero:"+frame.ControllerSeat,ResumeAt=MovementDestinationResume,Optional=true};
            state.Pending.CandidateCells=LegalCloakMoves(catalog,state,frame.ControllerSeat).Select(m=>m.Destination).ToList();
            Emit(state,command,"EffectMoveChoiceRequired",frame.ControllerSeat,state.Pending.Source,detail:frame.ResumeMoveMode.ToString());
        }

        private static void ChooseCloakMove(ContentCatalog catalog,GameState state,Command command)
        {
            Require(IsCloakMovementWindow(state) && state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_move" &&
                state.BeforeAction?.ControllerSeat==command.ActorSeat && state.Pending.ChooserSeat==command.ActorSeat &&
                command.MoveMode==MoveMode.Secondary && (command.Value=="" || command.Value=="skip"),"invalid_effect_move","请选择合法落点或不移动。");
            var frame=state.BeforeAction!;
            MoveOption? option=command.Value=="skip" ? null : LegalCloakMoves(catalog,state,command.ActorSeat).SingleOrDefault(m=>m.Destination==command.Destination);
            Require(command.Value=="skip" || option!=null,"invalid_effect_move","该格不是当前移动的合法落点。");
            if(frame.Stage=="destination")
            {
                state.BeforeAction=null;state.Phase=frame.ParentPhase;state.Pending=frame.ParentPending;state.ActiveSeat=frame.ParentActiveSeat;
                if(command.Value=="skip")
                {
                    Emit(state,command,"EffectMoveSkipped",command.ActorSeat,ActiveCard(state).CardId,detail:"declined:"+frame.ResumeMoveMode);
                    FinishAction(catalog,state,command);
                }
                else
                {
                    var resume=AsActor(command,frame.ControllerSeat,"",destination:command.Destination);resume.Kind=CommandKind.Move;resume.MoveMode=frame.ResumeMoveMode;
                    Move(catalog,state,resume,false);
                }
                return;
            }
            if(option==null)Emit(state,command,"EffectMoveSkipped",command.ActorSeat,frame.SourceCardId,detail:"declined");
            else
            {
                var unit=state.Units.Single(u=>u.Seat==command.ActorSeat);var origin=unit.Position;unit.Position=option.Destination;
                Emit(state,command,"UnitMoved",command.ActorSeat,frame.SourceCardId,detail:"BeforeAction");
                var moved=state.Events.Last();moved.From=origin;moved.To=unit.Position;moved.Path=option.Path;
            }
            FinishBeforeAction(catalog,state,command);
        }

        private static bool BeginUltimateRepeat(ContentCatalog catalog,GameState state,Command command)
        {
            var execution=state.Execution!;
            if(execution.UltimateRepeatUsed || execution.ActionStopped || string.IsNullOrEmpty(execution.AttackOutcome) ||
                catalog.Card(execution.CardId).PrimaryCategory!="基础攻击" || !UltimateRules.HasImmuneActionPrelude(catalog,state,execution.ControllerSeat))return false;
            execution.Attack=null;
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice {Id="ultimate-repeat:"+(state.Events.Count+1),Kind="primary_option",ChooserSeat=execution.ControllerSeat,
                Source=state.Players[execution.ControllerSeat].PurpleCardId!,ResumeAt=UltimateRepeatResume,Optional=true};
            Emit(state,command,"UltimateRepeatOffered",execution.ControllerSeat,state.Pending.Source);return true;
        }

        private static List<string> LegalUltimateRepeat(GameState state,int seat) => state.EngineVersion>=58 && state.Phase==Phase.EffectChoice &&
            state.Pending?.Kind=="primary_option" && state.Pending.ResumeAt==UltimateRepeatResume && state.Pending.ChooserSeat==seat &&
            state.Execution?.ControllerSeat==seat && !state.Execution.UltimateRepeatUsed && state.ActiveSeat==seat
                ? new List<string>{"repeat","finish"} : new List<string>();

        private static void ChooseUltimateRepeat(ContentCatalog catalog,GameState state,Command command,bool allowBeforeAction=true)
        {
            Require(LegalUltimateRepeat(state,command.ActorSeat).Contains(command.Value),"invalid_primary_option","请选择重复一次或结束基础攻击。");
            var previous=state.Execution!;
            if(command.Value=="finish")
            {
                previous.UltimateRepeatUsed=true;state.Pending=null;state.Phase=Phase.Action;
                Emit(state,command,"ActionRepeatSkipped",command.ActorSeat,previous.CardId);EndCardExecution(catalog,state,command);return;
            }
            if(allowBeforeAction && BeginBeforeAction(catalog,state,command))return;
            state.Execution=new CardExecution {CardId=previous.CardId,ProgramId=previous.ProgramId,ProgramVersion=previous.ProgramVersion,
                ControllerSeat=previous.ControllerSeat,FromDiscard=previous.FromDiscard,ActionInstanceId=state.EngineVersion>=63?NewActionInstance(state):null,UltimateRepeatUsed=true,UltimateRepeatExcludedTarget=previous.TargetUnitId};
            state.Pending=null;state.Phase=Phase.Action;
            Emit(state,command,"AttackRepeated",command.ActorSeat,previous.CardId,detail:"ultimate");
            ContinueCard(catalog,state,command);
        }
    }
}

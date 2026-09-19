#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static int MovementStepBudget(ContentCatalog catalog,GameState state,PrimaryProgram program) =>
            program.Instructions[state.Execution!.Cursor]==InstructionKind.PrimaryMovement
                ? catalog.Card(state.Execution.CardId).PrimaryValue+state.Players[state.Execution.ControllerSeat].MovementBonus : program.TextMoveDistance;
        private static bool PassesThroughTarget(GameState state,PrimaryProgram program) => program.Instructions[state.Execution!.Cursor]==InstructionKind.RequiredStraightMoveThroughEnemy;
        private static List<MoveOption> StrikeThroughMoves(ContentCatalog catalog,GameState state,UnitState source,PrimaryProgram program)
        {
            var result=new List<MoveOption>();var card=catalog.Card(state.Execution!.CardId);
            foreach(var target in state.Units.Where(u=>u.Team!=source.Team && u.Position.Distance(source.Position)==1))
                foreach(var option in MovementRules.StraightExact(catalog,state,source,program.TextMoveDistance,target.Id))
                {
                    if(option.Path[1]!=target.Position)continue;
                    var projected=new UnitState{Id=source.Id,Kind=source.Kind,Team=source.Team,Seat=source.Seat,Position=option.Destination};
                    if(CombatRules.Targets(catalog,state,projected,card,program).Contains(target.Id))result.Add(option);
                }
            return result.OrderBy(o=>o.Destination.X).ThenBy(o=>o.Destination.Y).ToList();
        }
        private static List<MoveOption> ChargeMoves(ContentCatalog catalog,GameState state,UnitState source,PrimaryProgram program)
        {
            if(PassesThroughTarget(state,program))return StrikeThroughMoves(catalog,state,source,program);
            var card=catalog.Card(state.Execution!.CardId);
            var paths=Enumerable.Range(program.TextMoveMinimum,program.TextMoveDistance-program.TextMoveMinimum+1)
                .SelectMany(distance=>MovementRules.StraightExact(catalog,state,source,distance));
            return paths.Where(option=>
            {
                // Check the attack from a prospective endpoint without moving the live unit during a query.
                var projected=new UnitState{Id=source.Id,Kind=source.Kind,Team=source.Team,Seat=source.Seat,Position=option.Destination};
                return CombatRules.Targets(catalog,state,projected,card,program).Count>0;
            }).OrderBy(o=>o.Destination.X).ThenBy(o=>o.Destination.Y).ToList();
        }
        private static bool BeginRequiredCharge(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            var options=source==null ? new List<MoveOption>() : ChargeMoves(catalog,state,source,program);
            if(options.Count==0)return false;
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice
            {
                Id="effect-move:"+(state.Events.Count+1),Kind="effect_move",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,UnitId=source!.Id,ResumeAt=PassesThroughTarget(state,program)?"strike_through_enemy":"charge_before_attack",Optional=false,
                CandidateCells=options.Select(o=>o.Destination).ToList()
            };
            Emit(state,command,"EffectMoveChoiceRequired",execution.ControllerSeat,execution.CardId,detail:program.TextMoveDistance.ToString());return true;
        }
        private static void MoveIntoAttackTargetCell(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            Require(execution.AttackTargetCell.HasValue,"missing_attack_position","攻击目标的位置记录缺失。");
            var destination=execution.AttackTargetCell!.Value;
            var option=source==null ? null : MovementRules.Reachable(catalog,state,source,program.TextMoveDistance).SingleOrDefault(o=>o.Destination==destination);
            if(option==null)
            {
                string reason=source==null ? "source_absent" : state.Units.Any(u=>u.Position==destination) ? "target_position_occupied" : "target_position_unreachable";
                Emit(state,command,"EffectMoveSkipped",execution.ControllerSeat,execution.CardId,detail:reason);return;
            }
            var origin=source!.Position;source.Position=destination;
            Emit(state,command,"UnitMoved",execution.ControllerSeat,execution.CardId,detail:"CardText");
            var moved=state.Events.Last();moved.From=origin;moved.To=destination;moved.Path=option.Path;
        }
        private static PrimaryProgram? TextMoveProgram(ContentCatalog catalog,GameState state)
        {
            var execution=state.Execution;
            if(state.EngineVersion<11 || execution==null) return null;
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            return program!=null && program.Id==execution.ProgramId && program.Version==execution.ProgramVersion &&
                execution.Cursor>=0 && execution.Cursor<program.Instructions.Count &&
                (program.Instructions[execution.Cursor]==InstructionKind.OptionalTextMove ||
                 program.Instructions[execution.Cursor]==InstructionKind.OptionalPreAttackTextMove ||
                 program.Instructions[execution.Cursor]==InstructionKind.OptionalTextMoveIfNoPreMove ||
                 program.Instructions[execution.Cursor]==InstructionKind.RequiredStraightMoveToAttack ||
                 program.Instructions[execution.Cursor]==InstructionKind.RequiredStraightMoveThroughEnemy ||
                 program.Instructions[execution.Cursor]==InstructionKind.PrimaryMovement ||
                 program.Instructions[execution.Cursor]==InstructionKind.RequiredStraightMoveIfAble) ? program : null;
        }
        public static List<MoveOption> LegalEffectMoves(ContentCatalog catalog,GameState state,int seat)
        {
            if(state.Pending?.ResumeAt=="defense_response_move")return LegalDefenseMoves(catalog,state,seat);
            if(state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="effect_move" || state.Pending.ChooserSeat!=seat ||
                state.Execution?.ControllerSeat!=seat) return new List<MoveOption>();
            var program=TextMoveProgram(catalog,state);
            var source=state.Units.SingleOrDefault(u=>u.Seat==seat && u.Id==state.Pending.UnitId);
            if(program==null || source==null)return new List<MoveOption>();
            if(program.Instructions[state.Execution!.Cursor]==InstructionKind.RequiredStraightMoveIfAble)return MovementRules.StraightExact(catalog,state,source,program.TextMoveDistance);
            return program.Instructions[state.Execution!.Cursor]==InstructionKind.RequiredStraightMoveToAttack || PassesThroughTarget(state,program)
                ? ChargeMoves(catalog,state,source,program) : MovementRules.Reachable(catalog,state,source,MovementStepBudget(catalog,state,program));
        }
        private static bool BeginTextMove(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var instruction=program.Instructions[execution.Cursor];
            if(instruction==InstructionKind.OptionalTextMoveIfNoPreMove && execution.PreAttackMoved)
            {
                Emit(state,command,"EffectMoveSkipped",execution.ControllerSeat,execution.CardId,detail:"pre_attack_move_used");
                return false;
            }
            // Only a primary Movement icon receives the movement bonus; fixed card text does not.
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            int budget=MovementStepBudget(catalog,state,program);
            bool requiredStraight=instruction==InstructionKind.RequiredStraightMoveIfAble;
            var options=source==null ? new List<MoveOption>() : requiredStraight ? MovementRules.StraightExact(catalog,state,source,budget) : MovementRules.Reachable(catalog,state,source,budget);
            if(options.Count==0)
            {
                Emit(state,command,"EffectMoveSkipped",execution.ControllerSeat,execution.CardId,detail:source==null ? "source_absent" : "no_destinations");
                return false;
            }
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice
            {
                Id="effect-move:"+(state.Events.Count+1),Kind="effect_move",ChooserSeat=execution.ControllerSeat,
                Source=execution.CardId,UnitId=source!.Id,ResumeAt=requiredStraight ? "required_straight_if_able" : instruction==InstructionKind.PrimaryMovement ? "primary_movement" : instruction==InstructionKind.OptionalPreAttackTextMove ? "before_attack" : "card_text_move",Optional=!requiredStraight,
                CandidateCells=options.Select(o=>o.Destination).ToList()
            };
            Emit(state,command,"EffectMoveChoiceRequired",execution.ControllerSeat,execution.CardId,detail:budget.ToString());
            return true;
        }
        private static void ChooseEffectMove(ContentCatalog catalog,GameState state,Command command)
        {
            if(state.Pending?.ResumeAt=="defense_response_move"){ChooseDefenseMove(catalog,state,command);return;}
            Require(state.Phase==Phase.EffectChoice && state.Pending?.Kind=="effect_move" && state.Pending.ChooserSeat==command.ActorSeat &&
                state.Execution?.ControllerSeat==command.ActorSeat && TextMoveProgram(catalog,state)!=null &&
                (command.Value=="" || command.Value=="skip" && state.Pending.Optional) && command.MoveMode==MoveMode.Secondary,
                "invalid_effect_move","请由牌文移动的操作者选择合法格或不移动；不能替换为快速移动。");
            var execution=state.Execution!;
            if(command.Value=="skip") Emit(state,command,"EffectMoveSkipped",command.ActorSeat,execution.CardId,detail:"declined");
            else
            {
                var option=LegalEffectMoves(catalog,state,command.ActorSeat).SingleOrDefault(o=>o.Destination==command.Destination);
                Require(option!=null,"invalid_effect_move","该格不是当前牌文移动的合法落点。");
                bool through=PassesThroughTarget(state,TextMoveProgram(catalog,state)!);
                if(through)execution.TargetUnitId=state.Units.Single(u=>u.Position==option!.Path[1]).Id;
                var unit=state.Units.Single(u=>u.Seat==command.ActorSeat);
                var origin=unit.Position;unit.Position=command.Destination;
                if(TextMoveProgram(catalog,state)!.Instructions[execution.Cursor]==InstructionKind.OptionalPreAttackTextMove) execution.PreAttackMoved=true;
                Emit(state,command,"UnitMoved",command.ActorSeat,execution.CardId,detail:TextMoveProgram(catalog,state)!.Instructions[execution.Cursor]==InstructionKind.PrimaryMovement ? "Primary" : "CardText");
                var moved=state.Events.Last();moved.From=origin;moved.To=unit.Position;moved.Path=option!.Path;
                if(through)Emit(state,command,"AttackTargetChosen",command.ActorSeat,execution.CardId,detail:execution.TargetUnitId);
            }
            execution.Cursor++;state.Pending=null;state.Phase=Phase.Action;
            ContinueCard(catalog,state,command);
        }
    }
}

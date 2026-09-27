#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;
namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static List<Hex> SelfPlacementCells(ContentCatalog catalog,GameState state,CardExecution execution)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            if(source==null)return new List<Hex>();
            var placement=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion)!.PlacementTarget;
            bool safe=placement==PlacementTargetKind.SafeInSkillRangeNearObstacle;
            // D-048 samples occupancy after leaving the origin; all hero/minion spawn types count.
            var blockedByEmptySpawn=new HashSet<Hex>();
            if(placement==PlacementTargetKind.EmptyNoSpawnAwayFromEmptySpawns)
                foreach(var spawn in catalog.Cells.Where(c=>c.Spawn.EndsWith("Spawn") && !state.Units.Any(u=>u.Id!=source.Id && u.Position==c.Position)))
                    foreach(var neighbor in spawn.Position.Neighbors())blockedByEmptySpawn.Add(neighbor);
            if(safe && !catalog.Cells.Any(c=>c.Obstacle && c.Position.Distance(source.Position)==1) && !state.Units.Any(u=>u.Id!=source.Id && u.Position.Distance(source.Position)==1))return new List<Hex>();
            int distance=(catalog.Card(execution.CardId).SubtypeValue??0)+(safe?state.Players[execution.ControllerSeat].RangeBonus:state.Players[execution.ControllerSeat].RangedBonus);
            // Placement has no traversal path; movement boundaries do not constrain its destination.
            return catalog.Cells.Where(c=>!c.Obstacle && (safe?!state.Units.Any(u=>u.Team!=source.Team && u.Position.Distance(c.Position)==1):c.Spawn=="empty") && c.Position.Distance(source.Position)>0 &&
                c.Position.Distance(source.Position)<=distance && !blockedByEmptySpawn.Contains(c.Position) && !state.Units.Any(u=>u.Position==c.Position))
                .Select(c=>c.Position).OrderBy(p=>p.X).ThenBy(p=>p.Y).ToList();
        }
        public static List<Hex> LegalPlacements(ContentCatalog catalog,GameState state,int seat)
        {
            if(state.Pending?.ResumeAt=="unit_placement")return LegalUnitPlacements(catalog,state,seat);
            var execution=state.Execution;
            if(state.EngineVersion<41 || state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="placement" ||
                state.Pending.ChooserSeat!=seat || execution?.ControllerSeat!=seat)return new List<Hex>();
            var program=CardPrograms.Primary(catalog.Card(execution.CardId),state.EngineVersion);
            if(program==null || program.Id!=execution.ProgramId || program.Version!=execution.ProgramVersion ||
                execution.Cursor<0 || execution.Cursor>=program.Instructions.Count || program.Instructions[execution.Cursor]!=InstructionKind.ChooseSelfPlacement)return new List<Hex>();
            return SelfPlacementCells(catalog,state,execution);
        }
        private static bool BeginSelfPlacement(ContentCatalog catalog,GameState state,Command command,CardExecution execution)
        {
            var cells=SelfPlacementCells(catalog,state,execution);
            if(cells.Count==0)return false;
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice
            {
                Id="placement:"+(state.Events.Count+1),Kind="placement",ChooserSeat=execution.ControllerSeat,
                UnitId="hero:"+execution.ControllerSeat,Source=execution.CardId,ResumeAt="self_placement",Optional=false,CandidateCells=cells
            };
            Emit(state,command,"PlacementChoiceRequired",execution.ControllerSeat,execution.CardId);
            return true;
        }
        private static void ChoosePlacement(ContentCatalog catalog,GameState state,Command command)
        {
            if(state.Pending?.ResumeAt=="unit_placement"){ChooseUnitPlacement(catalog,state,command);return;}
            Require(command.Value=="" && command.MoveMode==MoveMode.Secondary && LegalPlacements(catalog,state,command.ActorSeat).Contains(command.Destination),
                "invalid_placement","请由行动英雄选择不同的合法空格；放置不能跳过或替换为快速移动。");
            var execution=state.Execution!;var source=state.Units.Single(u=>u.Seat==command.ActorSeat);var origin=source.Position;
            source.Position=command.Destination;
            Emit(state,command,"UnitPlaced",command.ActorSeat,execution.CardId,detail:source.Id);
            var placed=state.Events.Last();placed.From=origin;placed.To=source.Position;
            execution.Cursor++;state.Pending=null;state.Phase=Phase.Action;ContinueCard(catalog,state,command);
        }
    }
}

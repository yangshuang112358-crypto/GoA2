#nullable enable
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static void PushAttackTargetIfAdjacent(ContentCatalog catalog,GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            var target=state.Units.SingleOrDefault(u=>u.Id==execution.TargetUnitId);
            if(source==null || target==null)return;
            var result=PushRules.AwayFromAdjacent(catalog,state,source,target,program.TextPushDistance);
            if(result==null)return;
            target.Position=result.Path.Last();
            Emit(state,command,"UnitPushed",target.Seat,execution.CardId,detail:"by:"+execution.ControllerSeat);
            var pushed=state.Events.Last();pushed.From=result.Path.First();pushed.To=target.Position;pushed.Path=result.Path;
            if(result.StopReason!="")Emit(state,command,"PushStopped",target.Seat,execution.CardId,detail:result.StopReason);
        }
    }
}

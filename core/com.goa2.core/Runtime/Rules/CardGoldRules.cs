#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules.Cards;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static List<GoldTransferOption> GoldTransfers(GameState state,CardExecution execution,PrimaryProgram program)
        {
            var result=new List<GoldTransferOption>();
            var source=state.Units.SingleOrDefault(u=>u.Seat==execution.ControllerSeat);
            if(source==null)return result;
            foreach(var unit in state.Units.Where(u=>u.Kind=="hero" && u.Seat.HasValue && u.Team!=source.Team && u.Position.Distance(source.Position)==1).OrderBy(u=>u.Seat))
            {
                int maximum=Math.Min(program.GoldMaximum,Math.Min(state.Players[unit.Seat!.Value].Gold,int.MaxValue-state.Players[execution.ControllerSeat].Gold));
                for(int amount=1;amount<=maximum;amount++)result.Add(new GoldTransferOption{TargetSeat=unit.Seat.Value,Amount=amount});
            }
            return result;
        }
        public static List<GoldTransferOption> LegalGoldTransfers(ContentCatalog catalog,GameState state,int seat)
        {
            var e=state.Execution;
            if(state.EngineVersion<37 || state.Phase!=Phase.EffectChoice || state.Pending?.Kind!="gold_transfer" || state.Pending.ChooserSeat!=seat || e==null || e.ControllerSeat!=seat || state.ActiveSeat!=seat)return new List<GoldTransferOption>();
            var p=CardPrograms.Primary(catalog.Card(e.CardId),state.EngineVersion);
            return p!=null && p.Id==e.ProgramId && p.Version==e.ProgramVersion && e.Cursor>=0 && e.Cursor<p.Instructions.Count && p.Instructions[e.Cursor]==InstructionKind.OptionalGoldTransfer
                ? GoldTransfers(state,e,p) : new List<GoldTransferOption>();
        }
        private static bool BeginGoldTransfer(GameState state,Command command,CardExecution execution,PrimaryProgram program)
        {
            var options=GoldTransfers(state,execution,program);
            if(options.Count==0){Emit(state,command,"GoldTransferSkipped",execution.ControllerSeat,execution.CardId,detail:"no_available_gold");return false;}
            state.Phase=Phase.EffectChoice;
            state.Pending=new PendingChoice{Id="gold-transfer:"+(state.Events.Count+1),Kind="gold_transfer",ChooserSeat=execution.ControllerSeat,Source=execution.CardId,
                CandidateSeats=options.Select(o=>o.TargetSeat).Distinct().ToList(),Optional=true,ResumeAt="card_gold_transfer"};
            Emit(state,command,"GoldTransferChoiceRequired",execution.ControllerSeat,execution.CardId);return true;
        }
        private static void ChooseGoldTransfer(ContentCatalog catalog,GameState state,Command command)
        {
            Require(state.EngineVersion>=37 && state.Phase==Phase.EffectChoice && state.ActiveSeat==command.ActorSeat && state.Pending?.Kind=="gold_transfer" && state.Pending.ChooserSeat==command.ActorSeat && state.Execution?.ControllerSeat==command.ActorSeat,
                "invalid_gold_transfer","请由当前牌文操作者选择金币转移。");
            bool skip=command.Value=="0" && command.TargetSeat==-1;
            Require(skip || LegalGoldTransfers(catalog,state,command.ActorSeat).Any(o=>o.TargetSeat==command.TargetSeat && o.Amount.ToString(CultureInfo.InvariantCulture)==command.Value),
                "invalid_gold_transfer","只能从相邻敌方英雄拿取合法数量的金币，或选择不拿取。");
            var execution=state.Execution!;
            if(skip)Emit(state,command,"GoldTransferSkipped",command.ActorSeat,execution.CardId,detail:"declined");
            else
            {
                int amount=int.Parse(command.Value,CultureInfo.InvariantCulture);
                state.Players[command.TargetSeat].Gold-=amount;state.Players[command.ActorSeat].Gold+=amount;
                Emit(state,command,"GoldTransferred",command.ActorSeat,execution.CardId,detail:"target:"+command.TargetSeat+"|amount:"+amount);
            }
            state.Pending=null;state.Phase=Phase.Action;execution.Cursor++;ContinueCard(catalog,state,command);
        }
    }
}

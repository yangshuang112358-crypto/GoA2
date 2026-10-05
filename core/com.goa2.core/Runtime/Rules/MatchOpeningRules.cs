#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Goa2.Domain;
namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static void StartDraft(GameState state,Command command)
        {
            Require(state.EngineVersion>=98 && state.Revision==0 && state.Opening==null && command.ActorSeat==0 && state.Players.All(p=>p.HeroId==null),"invalid_opening","只有房主可在新对局开始BP。");
            state.Opening=new MatchOpening{HostSeat=command.ActorSeat};Emit(state,command,"CoinTossStarted",command.ActorSeat,detail:state.Opening.TossId);
        }
        public static Team? DraftTeam(GameState state)
        {
            var opening=state.Opening;if(opening?.FirstTeam==null || opening.Status!="settled" || opening.DraftComplete)return null;
            int picks=state.Players.Count(p=>p.HeroId!=null);return picks==0 || picks==3?opening.FirstTeam:OtherTeam(opening.FirstTeam.Value);
        }
        private static void NextCoinToss(GameState state,Command command,string purpose)
        {
            var opening=state.Opening!;opening.Purpose=purpose;opening.TossNumber++;opening.Status="throwing";opening.Result=null;opening.FinalPose="";opening.RerollVotes.Clear();
            Emit(state,command,"CoinTossStarted",opening.HostSeat,detail:opening.TossId);
        }
        private static MatchOpening CurrentToss(GameState state,Command command,bool hostOnly)
        {
            Require(state.EngineVersion>=98 && state.Opening!=null,"no_coin_toss","当前没有待处理投币。");
            var opening=state.Opening!;Require(!hostOnly || command.ActorSeat==opening.HostSeat,"host_only","只有房主可以提交物理投币结果。");return opening;
        }
        private static void ReportCoinToss(GameState state,Command command)
        {
            var opening=CurrentToss(state,command,true);var fields=command.Value.Split('|');
            Require(fields.Length==3 && fields[0]==opening.TossId && opening.Status=="throwing","stale_coin_toss","投币阶段或编号已变化。");
            Require(fields[1]=="Blue" || fields[1]=="Red","invalid_coin_face","投币面无效。");
            var parts=fields[2].Split(',');Require(parts.Length==4,"invalid_coin_pose","需提供落定旋转。");var q=new double[4];
            for(int i=0;i<4;i++)Require(double.TryParse(parts[i],NumberStyles.Float,CultureInfo.InvariantCulture,out q[i]) && !double.IsNaN(q[i]) && !double.IsInfinity(q[i]) && Math.Abs(q[i])<=1.001,"invalid_coin_pose","投币旋转无效。");
            double norm=q.Sum(x=>x*x);Require(Math.Abs(norm-1)<.03,"invalid_coin_pose","投币旋转未归一化。");
            double up=1-2*(q[0]*q[0]+q[2]*q[2]);Require(fields[1]=="Red"?up>.85:up<-.85,"invalid_coin_face","硬币落面与姿态不一致。");
            opening.Result=fields[1]=="Red"?Team.Red:Team.Blue;opening.Status="settled";opening.FinalPose=fields[2];state.DecisionCoin=opening.Result.Value;
            Emit(state,command,"CoinTossSettled",command.ActorSeat,detail:opening.TossId+"|"+fields[1]);
            if(opening.Purpose=="draft")opening.FirstTeam=opening.Result;
            else {opening.OpeningComplete=true;StartPlanning(state,command);}
        }
        private static void MarkCoinStuck(GameState state,Command command)
        {
            var opening=CurrentToss(state,command,true);Require(command.Value==opening.TossId && opening.Status=="throwing","stale_coin_toss","投币已结束或已换轮。");
            opening.Status="stuck";Emit(state,command,"CoinTossStuck",command.ActorSeat,detail:opening.TossId);
        }
        private static void VoteCoinReroll(GameState state,Command command)
        {
            var opening=CurrentToss(state,command,false);Require(command.Value==opening.TossId && opening.Status=="stuck","stale_coin_toss","当前无需重投票。");
            if(!opening.RerollVotes.Contains(command.ActorSeat)){opening.RerollVotes.Add(command.ActorSeat);Emit(state,command,"CoinRerollVote",command.ActorSeat,detail:opening.TossId);}
            if(opening.RerollVotes.Count==4)NextCoinToss(state,command,opening.Purpose);
        }
    }
}

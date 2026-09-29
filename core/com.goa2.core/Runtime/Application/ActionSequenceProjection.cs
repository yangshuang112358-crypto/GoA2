#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules;

namespace Goa2.Application
{
    // Reconstruct only committed card uses from the authoritative journal. This projection
    // is identical for all seats; CardSelected, preview choices and raw commands never leave it.
    // Existing save/event formats and rule execution are unchanged.
    public static class ActionSequenceProjection
    {
        public static ActionSequenceView Build(ContentCatalog catalog, GameState state)
        {
            var output = new ActionSequenceView();
            int round=1, turn=1, start=-1, planning=0;
            for(int i=0;i<state.Events.Count;i++)
            {
                var e=state.Events[i];
                if(e.Kind=="PlanningStarted")
                {
                    planning=i;var parts=e.Detail.Split(':');
                    if(parts.Length==2){int.TryParse(parts[0],out round);int.TryParse(parts[1],out turn);}
                }
                if(e.Kind=="CardRevealed") {start=planning;output.Round=round;output.Turn=turn;}
            }
            if(start<0)return output;
            // Preserve the last revealed round during the following hidden planning phase.
            var journal=state.Events.Skip(start).ToList();
            int boundary=journal.FindIndex(1,e=>e.Kind=="PlanningStarted");
            if(boundary>=0)journal=journal.Take(boundary).ToList();
            var reveal=journal.FirstOrDefault(e=>e.Kind=="CardRevealed");
            if(reveal==null)return output;
            output.Id=state.MatchId+":"+reveal.Sequence;
            var all=new List<ActionCardView>();var roots=new List<ActionCardView>();
            var main=new Dictionary<int,ActionCardView>();
            var stack=new Stack<ActionCardView?>();var ultimates=new Stack<ActionCardView>();
            ActionCardView? current=null, defense=null;
            var commands=state.AcceptedCommands.ToDictionary(c=>c.Id,c=>c);
            ActionCardView? Node(GameEvent e,string role,ActionCardView? parent,string? cardId=null,int? owner=null)
            {
                string? id=cardId??e.CardId;int? seat=owner??e.Seat;
                if(id==null||!seat.HasValue||!catalog.Cards.Any(c=>c.Id==id))return null;
                var n=new ActionCardView {Id=e.Sequence+":"+role,Sequence=e.Sequence,Seat=seat.Value,CardId=id,Role=role,
                    ParentId=parent?.Id??"",Started=role!="main"};
                all.Add(n);if(parent==null)roots.Add(n);return n;
            }
            ActionCardView? Context() => ultimates.Count>0 ? ultimates.Peek() : defense??current;
            void Note(ActionCardView? n,string text){if(n!=null&&!n.Results.Contains(text))n.Results.Add(text);}
            string Hero(int seat) => catalog.Heroes.FirstOrDefault(h=>h.Id==state.Players.FirstOrDefault(p=>p.Seat==seat)?.HeroId)?.Name.Split('·').Last()??("英雄 "+(seat+1));
            string Unit(string id){if(id.StartsWith("hero:")&&int.TryParse(id.Substring(5),out int seat))return Hero(seat);return id.Contains("heavy")?"重型小兵":id.Contains("ranged")?"远程小兵":"小兵";}
            for(int i=0;i<journal.Count;i++)
            {
                var e=journal[i];
                if(e.Sequence<reveal.Sequence)continue;
                switch(e.Kind)
                {
                    case "CardRevealed":
                        var root=Node(e,"main",null);if(root!=null)main[root.Seat]=root;break;
                    case "HeroRespawnChoiceRequired":
                    case "ActionStarted":
                        if(e.Seat.HasValue&&main.TryGetValue(e.Seat.Value,out var acting))
                        {current=acting;defense=null;ultimates.Clear();acting.Started=true;acting.Sequence=e.Sequence;output.FocusId=acting.Id;}
                        break;
                    case "PrimaryActionStarted":Note(current,"执行主要行动");break;
                    case "AttackDeclared":
                        defense=null;Note(Context(),"攻击目标："+Unit(e.Detail));break;
                    case "DefenseChoiceRequired":Note(current,"等待 "+Hero(e.Seat??0)+" 防御");break;
                    case "CardDiscarded":
                        // A defense consumes the card once. Its discard is not a duplicate node.
                        bool forDefense=journal.Skip(i+1).TakeWhile(x=>x.CommandId==e.CommandId)
                            .Any(x=>x.Kind=="DefenseCalculated"&&x.Seat==e.Seat&&x.CardId==e.CardId);
                        ActionCardView? discarded;
                        if(forDefense && defense!=null && defense.CardId==e.CardId && defense.Seat==e.Seat && !defense.Results.Contains("已弃置"))discarded=defense;
                        else discarded=Node(e,forDefense?"defense":"discard",forDefense?current:Context());
                        if(discarded!=null){Note(discarded,"已弃置");discarded.Resolved=!forDefense;if(forDefense)defense=discarded;}
                        break;
                    case "DefenseCalculated":
                        if(defense!=null)Note(defense,e.Detail=="block"?"抵挡此次攻击":"防御结算："+e.Detail.Replace(":"," / "));break;
                    case "DefenseResolved":
                        if(defense!=null)
                        {
                            Note(defense,e.Detail=="success"?"防御成功":"防御失败");defense.Resolved=true;
                            // Ordinary defense is finished now. Only a real defense follow-up
                            // retains this parent; later attack movement belongs to the attacker.
                            bool continuation=journal.Skip(i+1).TakeWhile(x=>x.Kind!="AttackDeclared"&&x.Kind!="CardResolved"&&x.Kind!="DiscardAttackStarted")
                                .Any(x=>x.Kind=="DefenseResponseCompleted"&&x.CardId==defense.CardId&&x.Seat==defense.Seat);
                            var live=state.Execution?.DefenseResponse??state.BeforeAction?.ParentExecution?.DefenseResponse;
                            if(!continuation && !(live!=null&&live.SourceCardId==defense.CardId&&live.ControllerSeat==defense.Seat))defense=null;
                        }
                        else Note(current,"目标未使用防御牌");break;
                    case "DefenseResponseCompleted":defense=null;break;
                    case "UltimateTriggered":
                        // Before-defense abilities start after a committed Defend command; the
                        // actual defense card is already chosen, so its purple child can nest now.
                        if(e.Detail=="before:Defend" && commands.TryGetValue(e.CommandId,out var committed) && committed.Kind==CommandKind.Defend)
                            defense=Node(e,"defense",current,committed.Value,committed.ActorSeat);
                        var ultimate=Node(e,"ultimate",Context());if(ultimate!=null){ultimates.Push(ultimate);Note(ultimate,"触发紫卡能力");}break;
                    case "UltimateCompleted":
                        if(ultimates.Count>0){var u=ultimates.Pop();u.Resolved=true;Note(u,e.Detail=="no_targets"?"没有合法目标":e.Detail=="empty_hand"?"目标无手牌":"处理完成");}break;
                    case "DiscardAttackStarted":
                        stack.Push(current);current=Node(e,"reaction",null);defense=null;ultimates.Clear();
                        if(current!=null){output.FocusId=current.Id;Note(current,"由反击效果发动");}break;
                    case "DiscardAttackCompleted":
                        if(current!=null){current.Resolved=true;Note(current,"反击结算完成");}
                        current=stack.Count>0?stack.Pop():null;defense=null;break;
                    case "ActionRepeated":case "AttackRepeated":
                        Note(current,"再次执行 · 新目标");
                        // A resumed root after a reaction is focused only when it actually resumes.
                        if(current!=null)output.FocusId=current.Id;defense=null;break;
                    case "CardResolved":
                        if(e.Seat.HasValue&&main.TryGetValue(e.Seat.Value,out var done))
                        {done.Resolved=true;Note(done,"本牌结算完成");}
                        defense=null;ultimates.Clear();break;
                    case "ActionPassed":Note(current,"跳过行动");break;
                    case "AttackResolved":Note(current,e.Detail.StartsWith("defended")?"攻击被抵挡":"本次攻击结算完成");break;
                    case "EffectTargetChosen":Note(Context(),"选择："+Unit(e.Detail));break;
                    case "UnitMoved":Note(Context(),(e.Detail=="Secondary"?"次要移动":e.Detail=="Fast"?"快速移动":"移动")+"至 "+e.To);break;
                    case "UnitPlaced":case "UnitPushed":Note(Context(),"位移至 "+e.To);break;
                    case "MinionDefeated":Note(Context(),"击败 "+Unit(e.Detail));break;
                    case "MinionRemoved":case "EffectMinionRemoved":Note(Context(),"移除小兵");break;
                    case "HeroDefeated":Note(Context(),Hero(e.Seat??0)+" 被击败");break;
                    case "CardRecovered":
                        var recovered=Node(e,"recover",Context());if(recovered!=null){recovered.Resolved=true;Note(recovered,"取回手牌");}break;
                    case "ForcedPaymentDefeat":case "RetaliationDiscardDeclined":Note(Context(),"选择不弃牌，按牌文被击败");break;
                    case "CardEffectStopped":Note(current,"本牌后续步骤结束");break;
                }
            }
            foreach(var n in all)
                n.Initiative=catalog.Card(n.CardId).Initiative+PassiveRules.Initiative(state,n.Seat);
            var ordered=roots.Where(n=>n.Started).OrderBy(n=>n.Sequence).ToList();
            var pending=roots.Where(n=>!n.Started).OrderByDescending(n=>n.Initiative).ThenBy(n=>n.Seat).ToList();
            Team coin=state.DecisionCoin;bool first=true;
            while(pending.Count>0)
            {
                int initiative=pending[0].Initiative;var tied=pending.Where(n=>n.Initiative==initiative).ToList();
                Team team=state.Players[tied[0].Seat].Team;
                if(first&&state.Pending?.Kind=="initiative"&&state.Pending.CandidateSeats.Count>0)
                    team=state.Players[state.Pending.CandidateSeats[0]].Team; // Coin already flipped by rules.
                else if(tied.Select(n=>state.Players[n.Seat].Team).Distinct().Count()>1){team=coin;coin=coin==Team.Blue?Team.Red:Team.Blue;}
                var chosen=tied.First(n=>state.Players[n.Seat].Team==team);ordered.Add(chosen);pending.Remove(chosen);first=false;
            }
            void Append(ActionCardView n){output.Cards.Add(n);foreach(var child in all.Where(x=>x.ParentId==n.Id).OrderBy(x=>x.Sequence))Append(child);}
            foreach(var n in ordered)Append(n);
            return output;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
namespace Goa2.Ai
{
    public static class ObservationProjector
    {
        public static string CategoryId(string value)=>value switch {
            "基础攻击"=>"basic_attack","攻击"=>"attack","防御"=>"defense","移动"=>"movement",
            "基础技能"=>"basic_skill","技能"=>"skill","终极技能"=>"ultimate",
            _=>throw new InvalidOperationException("unknown_primary_category:"+value) };
        public static string BonusId(string value) => value switch {
            ""=>"", "攻击"=>"attack", "防御"=>"defense", "移动"=>"movement", "先攻"=>"initiative",
            "范围"=>"skill_range", "远程"=>"attack_range", _=>throw new InvalidOperationException("unknown_bonus:"+value) };
        private static ObservedBonuses Bonuses(Dictionary<string,int>? source)
        {
            var d=(source??new Dictionary<string,int>()).ToDictionary(p=>BonusId(p.Key),p=>p.Value);
            int V(string key)=>d.TryGetValue(key,out int v)?v:0;
            return new ObservedBonuses {Attack=V("attack"),Defense=V("defense"),Movement=V("movement"),Initiative=V("initiative"),SkillRange=V("skill_range"),AttackRange=V("attack_range")};
        }
        private static ObservedUnit Unit(UnitState u,GameView v)=>new ObservedUnit {Id=u.Id,Kind=u.Kind,Team=u.Team.ToString(),Seat=u.Seat,Position=u.Position,Removable=v.RemovableMinions.Contains(u.Id)};
        private static ObservedUpgrade Upgrade(UpgradeRecord u,StableIds ids)=>new ObservedUpgrade {
            Round=u.Round,HeroLevel=u.HeroLevel,CardLevel=u.CardLevel,Amount=u.Amount,Previous=ids.Card(u.PreviousCardId),
            Selected=ids.Card(u.SelectedCardId),Rejected=ids.Card(u.RejectedCardId),Bonus=BonusId(u.Bonus) };
        private static void EventFacts(GameEvent e,ObservedEvent output)
        {
            // Parse only reviewed public values. In particular never copy Detail wholesale:
            // some public lifecycle events contain internal frame/effect identifiers.
            int N(string s)=>int.Parse(s,System.Globalization.CultureInfo.InvariantCulture);
            string[] parts;
            switch(e.Kind)
            {
                case "MinionDefeated":case "MinionRemoved":case "MinionSpawned":case "AttackDeclared":case "AttackTargetChosen":
                case "EffectMinionRemoved":case "UnitPlaced":case "UnitsSwapped":case "UnitDisplacementPrevented":
                case "OtherMoveTargetChosen":case "MinionReturnPlaced":case "MinionReturnMoved":case "MinionReturnCompleted":
                case "MinionProtectionChoiceRequired":case "MinionDefeatPrevented":case "MinionProtectionDeclined":case "MinionBattleStoppedByHero":
                case "EffectTargetChosen":case "ActionRepeated":case "AttackRepeated":
                    if(e.Detail.StartsWith("by:",StringComparison.Ordinal))goto case "UnitMoved";
                    if(e.Detail=="skip" || e.Detail=="ultimate")output.Value=e.Detail; else output.Unit=e.Detail;break;
                case "UnitMoved":case "UnitPushed":
                    parts=e.Detail.Split('|');
                    if(parts.Length==2 && parts[0].StartsWith("by:",StringComparison.Ordinal) && parts[1].StartsWith("unit:",StringComparison.Ordinal))
                    {output.OtherSeat=N(parts[0].Substring(3));output.Unit=parts[1].Substring(5);}
                    else if(parts.Length==1 && e.Detail.StartsWith("by:",StringComparison.Ordinal))output.OtherSeat=N(e.Detail.Substring(3));
                    else output.Value=e.Detail;break;
                case "HeroDeployed":
                    parts=e.Detail.Split(',');if(parts.Length==2)output.To=new Hex(N(parts[0]),N(parts[1]));break;
                case "HeroDefeated":output.OtherSeat=N(e.Detail.Substring(3));break;
                case "GoldAwarded":case "AssistGoldAwarded":case "CrystalDamaged":case "RoundCompensationGranted":case "CardsRecalled":case "MinionsCleared":
                    output.Amount=N(e.Detail);break;
                case "GoldTransferred":
                    parts=e.Detail.Split('|');output.OtherSeat=N(parts[0].Substring(7));output.Amount=N(parts[1].Substring(7));break;
                case "HeroLeveled":case "MinionBattleCounted":case "ActionMinionBattleCounted":
                    parts=e.Detail.Split(':');output.Amount=N(parts[0]);output.Amount2=N(parts[1]);output.Amount3=N(parts[2]);break;
                case "AttackResolved":
                    int colon=e.Detail.IndexOf(':');if(colon>=0){output.Value=e.Detail.Substring(0,colon);output.Unit=e.Detail.Substring(colon+1);}break;
                case "HeroChosen":case "FrontlineAdvanced":case "FrontlineCompleted":case "FrontlineMarkGained":case "ActionMinionBattleCompleted":
                case "DiscardColorShown":case "RecoveredColorShown":case "DefenseResolved":case "PrimaryOptionChosen":case "PoisonApplied":
                    output.Value=e.Detail;break;
                case "CardSwapColorsShown":parts=e.Detail.Split(':');output.Value=parts[0];output.SecondaryValue=parts[1];break;
                case "DecisionCoinFlipped":parts=e.Detail.Split(new[]{" wins; now "},StringSplitOptions.None);output.Value=parts[0];output.SecondaryValue=parts[1];break;
                case "CoinTossSettled":output.Value=e.Detail.Split('|').Last();break;
            }
        }
        // Explicit allowlist from the ALREADY masked seat projection. Never serialize a raw view or state.
        public static Observation Project(GameView own,int seat,StableIds ids,PublicRuleProfile rules)
        {
            ObservedCard Card(CardInstance c)=>new ObservedCard {Id=ids.Card(c.CardId),Zone=c.Zone.ToString(),PlayedRound=c.PlayedRound,PlayedTurn=c.PlayedTurn};
            var keys=own.ActionSequence.Cards.Select((c,i)=>(c.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
            var history=new List<ObservedEvent>(); int round=1,turn=1;
            foreach(var e in own.Events.Where(e=>e.PrivateTo==null && !e.Kind.StartsWith("Debug",StringComparison.Ordinal)))
            {
                if(e.Kind=="PlanningStarted")
                {
                    var parts=e.Detail.Split(':');
                    if(parts.Length!=2 || !int.TryParse(parts[0],out round) || !int.TryParse(parts[1],out turn))
                        throw new InvalidOperationException("unsupported_public_round_event");
                }
                var fact=new ObservedEvent {Ordinal=history.Count,Round=round,Turn=turn,Kind=e.Kind,Card=ids.Card(e.CardId),Seat=e.Seat,From=e.From,To=e.To,Path=e.Path.ToList()};
                EventFacts(e,fact);history.Add(fact);
            }
            return new Observation {
                Rules=rules.CopyFor(own.VictoryMarksRequired),Seat=seat,Round=own.Round,Turn=own.Turn,Phase=own.Phase.ToString(),Decision=own.Pending?.Kind??own.Phase.ToString(),
                ActiveSeat=own.ActiveSeat,AttackRange=own.AttackRange,CombatRegion=own.CombatRegion,Coin=own.DecisionCoin.ToString(),
                BlueCaptain=own.BlueCaptain,RedCaptain=own.RedCaptain,RoundEndStage=own.RoundEndStage,
                RemainingMinionRemovals=own.RemainingMinionRemovals,UpgradingSeats=own.UpgradingSeats.ToList(),
                Response=own.Pending==null?null:new ObservedResponse {Source=ids.Value(own.Pending.Source),Unit=own.Pending.UnitId,Optional=own.Pending.Optional},
                Opening=own.Opening==null?null:new ObservedOpening {Purpose=own.Opening.Purpose,Status=own.Opening.Status,FirstTeam=own.Opening.FirstTeam?.ToString()??"",Result=own.Opening.Result?.ToString()??"",DraftTeam=own.DraftTeam?.ToString()??"",DraftComplete=own.Opening.DraftComplete,OpeningComplete=own.Opening.OpeningComplete},
                Sequence=new ObservedSequence {Round=own.ActionSequence.Round,Turn=own.ActionSequence.Turn,
                    Cards=own.ActionSequence.Cards.Select((c,i)=>new ObservedActionCard {Key=i,Order=i,Parent=string.IsNullOrEmpty(c.ParentId)?(int?)null:keys[c.ParentId],Card=ids.Card(c.CardId),Role=c.Role,Seat=c.Seat,Initiative=c.Initiative,Started=c.Started,Resolved=c.Resolved,Focused=c.Id==own.ActionSequence.FocusId}).ToList()},
                Attack=own.Attack==null?null:new ObservedAttack {Card=ids.Card(own.Attack.SourceCardId),Target=own.Attack.TargetUnitId,Attacker=own.Attack.AttackerSeat,Defender=own.Attack.DefenderSeat,Base=own.Attack.BaseAttack,Bonus=own.Attack.AttackBonus,Support=own.Attack.EnemySupport,Guard=own.Attack.FriendlyGuard,Final=own.Attack.FinalAttack,TextBonus=own.Attack.CardTextBonus,UltimateBonus=own.Attack.UltimateBonus,Ranged=own.Attack.Ranged,Unblockable=own.Attack.Unblockable,TextSources=own.Attack.CardTextSourceUnits.ToList(),SupportSources=own.Attack.EnemySupportSources.ToList(),GuardSources=own.Attack.FriendlyGuardSources.ToList()},
                BlueCrystal=own.BlueCrystal,RedCrystal=own.RedCrystal,BlueMarks=own.BlueMarks,RedMarks=own.RedMarks,
                Players=own.Players.Select(p=>new ObservedPlayer {Seat=p.Seat,Team=p.Team.ToString(),Hero=p.HeroId??"",Purple=ids.Card(p.PurpleCardId),Level=p.Level,Gold=p.Gold,Confirmed=p.Confirmed,AwaitingRespawn=p.AwaitingRespawn,Poisoned=p.IsPoisoned,PoisonDefense=p.PoisonIncludesDefense,Petrified=p.IsPetrified,BasicAttackBonus=p.BasicAttackBonus,BasicAttackRangeBonus=p.BasicAttackRangeBonus,Permanent=Bonuses(p.PermanentBonuses),Effective=Bonuses(p.EffectiveBonuses),Cards=(p.Seat==seat?own.OwnCards:p.PublicCards).Select(Card).ToList()}).ToList(),
                Units=own.Units.Select(u=>Unit(u,own)).ToList(),PendingSpawns=own.PendingSpawns.Select(u=>Unit(u,own)).ToList(),OwnUpgrades=own.OwnUpgradeHistory.Select(u=>Upgrade(u,ids)).ToList(),PublicHistory=history,
                Effects=own.Effects.OrderBy(e=>e.CreationOrder).Select((e,i)=>new ObservedEffect {Kind=e.Kind.ToString(),Card=ids.Card(e.SourceCardId),SourceUnit=e.SourceUnitId,ProtectedUnit=e.ProtectedUnitId,Controller=e.ControllerSeat,CreatedRound=e.CreatedRound,CreatedTurn=e.CreatedTurn,Order=i,StartRound=e.Window.StartRound,StartTurn=e.Window.StartTurn,EndRound=e.Window.EndRound,EndTurn=e.Window.EndTurn,ExemptSeat=e.ExemptControllerSeat,BaseRadius=e.BaseRadius,PersistsThroughDefeat=e.PersistsThroughDefeat,Duration=e.Duration.ToString(),AreaKind=e.AreaKind.ToString(),Area=own.EffectAreas[e.Id].ToList()}).ToList()
            };
        }
        public static CandidateFacts Facts(GameView v,CommandKind kind,string value,Hex at,MoveMode mode,StableIds ids)
        {
            var result=new CandidateFacts();
            var moves=kind==CommandKind.ChooseEffectMove?v.EffectMoves:kind==CommandKind.Move?(mode==MoveMode.Fast?v.FastMoves:v.SecondaryMoves):new List<MoveOption>();
            var move=moves.FirstOrDefault(m=>m.Destination==at); if(move!=null && value!="begin" && value!="skip") result.Path=move.Path.ToList();
            if(kind==CommandKind.Defend)
            {
                var d=v.DefenseOptions.Single(x=>x.CardId==value); var a=d.Assessment;
                result.Defense=new ObservedDefense {Primary=d.Primary,Blocked=a.Blocked,IgnoresMinions=a.IgnoresMinions,Base=a.BaseDefense,Bonus=a.DefenseBonus,Final=a.FinalDefense,AttackCompared=a.AttackCompared};
            }
            if(kind==CommandKind.ChooseUpgrade)
            {
                var u=v.UpgradeOptions.Single(x=>x.CardId==value);
                result.Upgrade=new ObservedUpgrade {Round=v.Round,HeroLevel=u.HeroLevel,CardLevel=u.CardLevel,Amount=1,Previous=ids.Card(u.PreviousCardId),Selected=ids.Card(u.CardId),Rejected=ids.Card(u.RejectedCardId),Bonus=BonusId(u.Bonus)};
            }
            if(kind==CommandKind.ChooseMinionReturn)
            {
                var r=v.MinionReturns.Single(x=>x.UnitId==value && x.Destination==at);result.Place=r.Place;result.RemainingDistance=r.RemainingDistance;
            }
            if(kind==CommandKind.ChooseGoldTransfer)result.TransferAmount=int.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
            if(kind==CommandKind.BeginPrimary && v.PrimaryPreview!=null)
            {result.PreviewKind=v.PrimaryPreview.Kind;result.PreviewOptional=v.PrimaryPreview.Optional;result.PreviewTargets=v.PrimaryPreview.Targets.ToList();result.PreviewCells=v.PrimaryPreview.Cells.ToList();}
            return result;
        }
    }
}

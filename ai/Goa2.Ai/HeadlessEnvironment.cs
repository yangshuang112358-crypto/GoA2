using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Newtonsoft.Json;

namespace Goa2.Ai
{
    public sealed class HeadlessEnvironment
    {
        private readonly GameSession session;
        private readonly StableIds ids;
        private readonly StableRandom coin;
        private readonly string episode;
        private readonly PublicRuleProfile rules;
        private Dictionary<string,Command> intents = new Dictionary<string,Command>();
        private Decision? outstanding;
        public Action<Command,GameView>? Accepted;
        public GameView Spectator => session.View(null); // Host/evaluator only, never an IPolicy input.
        public string ExportSave() => session.ExportSave();
        public bool Terminated => Spectator.Phase == Phase.Finished;
        public Command? LastAttempt { get; private set; }
        public HeadlessEnvironment(ContentCatalog catalog, int seed, string episode)
        {
            this.episode=episode; ids=new StableIds(catalog); rules=PublicRuleProfile.From(catalog); coin=new StableRandom(seed ^ 0x43e19a);
            session=LocalGameFactory.Create(catalog,"scenario:"+episode,new[]{"AI 0","AI 1","AI 2","AI 3"},seed);
        }
        // Test/teaching adapter; authority is still validated by GameSession. Never use a mutated origin as a formal match.
        public HeadlessEnvironment(ContentCatalog catalog, GameSession session, int seed, string episode)
        { this.session=session; this.episode=episode; ids=new StableIds(catalog); rules=PublicRuleProfile.From(catalog); coin=new StableRandom(seed ^ 0x43e19a); }
        private Command Intent(GameView v, int seat, CommandKind kind, string value="", int target=-1, Hex at=default, MoveMode mode=MoveMode.Secondary) =>
            new Command { Id=episode+":"+(v.Revision+1), MatchId=v.MatchId, ExpectedRevision=v.Revision, ActorSeat=seat, Kind=kind, Value=value, TargetSeat=target, Destination=at, MoveMode=mode };
        private void Execute(Command command)
        {
            LastAttempt=command;
            var result=session.Execute(command.ActorSeat,command);
            if(!result.Accepted || result.Duplicate) throw new InvalidOperationException("candidate_rejected:"+result.Code+":"+result.Message);
            Accepted?.Invoke(command,result.View);
        }
        public Decision? Next()
        {
            if(outstanding!=null) return Clone(outstanding);
            var v=Spectator;
            if(v.Revision==0) { Execute(Intent(v,0,CommandKind.StartDraft)); v=Spectator; }
            if(v.Opening?.Status=="throwing")
            {
                bool red=coin.Next(2)==0;
                Execute(Intent(v,v.Opening.HostSeat,CommandKind.ReportCoinToss,v.Opening.TossId+"|"+(red?"Red|0,0,0,1":"Blue|1,0,0,0")));
                v=Spectator;
            }
            if(v.Phase==Phase.Finished) return null;
            if(v.Opening?.Status=="stuck") throw new InvalidOperationException("unsupported_decision:physical_stuck_requires_votes");
            if(v.Players.Count!=4) throw new InvalidOperationException("unsupported_capacity:engine98_requires_four_seats");
            int seat;
            if(v.Pending!=null) seat=v.Pending.ChooserSeat;
            else if(v.Phase==Phase.HeroSelection) seat=v.Players.Where(p=>p.HeroId==null && (v.Opening==null || p.Team==v.DraftTeam)).Select(p=>p.Seat).First();
            else if(v.Phase==Phase.Deployment)
            {
                var target=v.Players.First(p=>!v.Units.Any(u=>u.Seat==p.Seat));
                seat=target.Team==Team.Blue?v.BlueCaptain:v.RedCaptain;
            }
            else if(v.Phase==Phase.Planning) seat=v.Players.First(p=>!p.Confirmed && p.HandCount>0).Seat;
            else if(v.Phase==Phase.RoundEnd) seat=v.UpgradingSeats.Count>0?v.UpgradingSeats.Min():0;
            else if(v.Phase==Phase.Action && v.ActiveSeat.HasValue) seat=v.ActiveSeat.Value;
            else throw new InvalidOperationException("unsupported_decision:"+v.Phase);
            var own=session.View(seat);
            outstanding=new Decision { Revision=own.Revision, Observation=ObservationProjector.Project(own,seat,ids,rules) };
            intents=new Dictionary<string,Command>(StringComparer.Ordinal);
            void Add(CommandKind kind,string value="",int target=-1,Hex at=default,MoveMode mode=MoveMode.Secondary,bool cell=false,bool defense=false)
            {
                var semantic=ids.Value(value); string key=StableIds.Action(kind,semantic,target,at,mode);
                if(intents.ContainsKey(key)) throw new InvalidOperationException("duplicate_action_id:"+key);
                intents.Add(key,Intent(own,seat,kind,value,target,at,mode));
                outstanding.Actions.Add(new Candidate { Id=key, Kind=kind.ToString(), Value=semantic, TargetSeat=target, Destination=at, Mode=mode.ToString(), HasDestination=cell, SuccessfulDefense=defense, ImmediateSkip=kind==CommandKind.BeginPrimary && own.PrimaryImmediatelySkips, Facts=ObservationProjector.Facts(own,kind,value,at,mode,ids) });
            }
            void Values(CommandKind kind,IEnumerable<string> values,bool skip=false) { foreach(var x in values) Add(kind,x); if(skip) Add(kind,"skip"); }
            void Cells(CommandKind kind,IEnumerable<Hex> cells,bool skip=false) { foreach(var x in cells) Add(kind,at:x,cell:true); if(skip) Add(kind,"skip"); }
            var p=own.Pending;
            if(p!=null)
            {
                switch(p.Kind)
                {
                    case "initiative": foreach(var target in p.CandidateSeats) Add(CommandKind.ChooseInitiative,target:target); break;
                    case "hero_respawn": Cells(CommandKind.RespawnHero,own.RespawnCells); break;
                    case "attack_target": Values(CommandKind.ChooseAttackTarget,own.AttackTargets,p.Optional); break;
                    case "minion_protection": Values(CommandKind.ChooseMinionProtection,own.MinionProtectionCards,true); break;
                    case "primary_option": Values(CommandKind.ChoosePrimaryOption,own.PrimaryOptions); break;
                    case "effect_target": case "effect_minion": Values(CommandKind.ChooseEffectTarget,own.EffectTargets,p.Optional); break;
                    case "defense": foreach(var d in own.DefenseOptions) Add(CommandKind.Defend,d.CardId,defense:d.Assessment.Successful); Add(CommandKind.DeclineDefense); break;
                    case "forced_discard": Values(CommandKind.ForcedDiscard,own.ForcedDiscardCards); if(own.CanDeclineRetaliationDiscard) Add(CommandKind.DeclineRetaliationDiscard); break;
                    case "optional_discard": Values(CommandKind.ChooseOptionalDiscard,own.OptionalDiscardCards,true); break;
                    case "effect_move": Cells(CommandKind.ChooseEffectMove,own.EffectMoves.Select(m=>m.Destination),p.Optional); break;
                    case "placement": Cells(CommandKind.ChoosePlacement,own.Placements,p.Optional); break;
                    case "minion_return": foreach(var r in own.MinionReturns) Add(CommandKind.ChooseMinionReturn,r.UnitId,at:r.Destination,cell:true); break;
                    case "gold_transfer": foreach(var g in own.GoldTransfers) Add(CommandKind.ChooseGoldTransfer,g.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),g.TargetSeat); Add(CommandKind.ChooseGoldTransfer,"0"); break;
                    case "card_swap": Values(CommandKind.ChooseCardSwap,own.CardSwapOptions,p.Optional); break;
                    case "recover_discard": Values(CommandKind.ChooseRecoveredCard,own.RecoverableCards,p.Optional); break;
                    case "discard_attack": Values(CommandKind.ChooseDiscardAttack,own.DiscardAttackCards,p.Optional); break;
                    case "round_minion_removal": case "action_minion_removal": Values(CommandKind.ChooseRoundMinionRemoval,own.RoundMinionRemovals); break;
                    case "minion_spawn": foreach(var pair in own.SpawnChoices) foreach(var at in pair.Value) Add(CommandKind.ChooseMinionSpawn,pair.Key,at:at,cell:true); break;
                    default: throw new InvalidOperationException("unsupported_decision:"+p.Kind);
                }
            }
            else if(own.Phase==Phase.HeroSelection) Values(CommandKind.ChooseHero,own.AvailableHeroes);
            else if(own.Phase==Phase.Deployment) foreach(var d in own.Deployments) foreach(var at in d.Value) Add(CommandKind.DeployHero,target:d.Key,at:at,cell:true);
            else if(own.Phase==Phase.Planning)
            {
                // A bot commits one selection; UI-only reselection/cancellation would create an unbounded no-op loop.
                if(own.OwnCards.Any(c=>c.Zone==CardZone.Selected)) Add(CommandKind.ConfirmCard);
                else Values(CommandKind.SelectCard,own.OwnCards.Where(c=>c.Zone==CardZone.InHand).Select(c=>c.CardId));
            }
            else if(own.Phase==Phase.RoundEnd)
            {
                Values(CommandKind.ChooseUpgrade,own.UpgradeOptions.Select(o=>o.CardId));
                if(own.CanResolveRoundEnd) Add(CommandKind.ResolveRoundEnd);
            }
            else if(own.Phase==Phase.Action)
            {
                if(own.CanBeginPrimary) Add(CommandKind.BeginPrimary);
                foreach(var m in own.SecondaryMoves) Add(CommandKind.Move,at:m.Destination,cell:true);
                foreach(var m in own.FastMoves) Add(CommandKind.Move,at:m.Destination,mode:MoveMode.Fast,cell:true);
                if(own.CanStartSecondaryMoveWithPrelude) Add(CommandKind.Move,"begin");
                if(own.CanStartFastMoveWithPrelude) Add(CommandKind.Move,"begin",mode:MoveMode.Fast);
                if(own.CanPass) Add(CommandKind.Pass);
            }
            if(intents.Count==0) throw new InvalidOperationException("unsupported_decision:no_candidates:"+outstanding.Observation.Decision);
            outstanding.Actions=outstanding.Actions.OrderBy(a=>a.Id,StringComparer.Ordinal).ToList();
            return Clone(outstanding);
        }
        public void Submit(long revision,string actionId)
        {
            if(outstanding==null || revision!=outstanding.Revision) throw new InvalidOperationException("stale_decision");
            if(!intents.TryGetValue(actionId,out var command)) throw new InvalidOperationException("unknown_action");
            Execute(command); outstanding=null; intents.Clear();
        }
        private static Decision Clone(Decision value) => JsonConvert.DeserializeObject<Decision>(JsonConvert.SerializeObject(value))!;
    }
}

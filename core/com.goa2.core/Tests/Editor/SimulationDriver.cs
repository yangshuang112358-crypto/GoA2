#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;

namespace Goa2.Tests
{
    public sealed class SimulationSummary
    {
        public bool Passed,ScenarioReplayPassed;
        public int Seed,StepLimit,Steps,FinalRound,EngineVersion,UnauthorizedChecks,DuplicateChecks;
        public bool Sandbox;
        public string StopReason="",Failure="",ContentHash="",FinalStateHash="";
        public List<int> RestoreCheckpoints=new List<int>();
        public Dictionary<string,int> Commands=new Dictionary<string,int>();
        public Dictionary<string,int> Events=new Dictionary<string,int>();
        public List<string> UnimplementedPrimaryCards=new List<string>();
        public List<string> ChoicesSeen=new List<string>();
        public Command? LastAttempt;
    }
    internal sealed class SimulationDriver
    {
        public SimulationSummary Report { get; }=new SimulationSummary();
        public ScenarioDefinition Definition { get; }=new ScenarioDefinition();
        public string FinalSave { get; private set; }="";
        private readonly ContentCatalog catalog;
        private readonly int limit;
        private readonly string? output;
        private readonly JsonStateCodec codec=new JsonStateCodec();
        private readonly Queue<ScenarioStep> setup=new Queue<ScenarioStep>();
        private GameSession session;
        private uint random;
        private bool sandboxPrepared;
        private string beforeSave="";
        public SimulationDriver(ContentCatalog catalog,int seed,int steps,bool sandbox,string? output)
        {
            this.catalog=catalog; limit=steps; this.output=output; random=unchecked((uint)seed)^0x9e3779b9u;
            Definition.SchemaVersion=1; Definition.Id=$"simulation-{seed}-{(sandbox ? "sandbox" : "formal")}";
            Definition.Name=$"随机长对局：种子{seed}，{(sandbox ? "快速调试" : "正式确认")}";
            Definition.Seed=seed; Definition.Sandbox=sandbox; Definition.VerifyReplayAfterEachStep=false;
            session=LocalGameFactory.Create(catalog,"scenario:"+Definition.Id,Definition.Players,seed,sandbox);
            Report.Seed=seed; Report.StepLimit=steps; Report.Sandbox=sandbox;
            Report.EngineVersion=GameState.CurrentEngineVersion; Report.ContentHash=catalog.Hash;
        }
        private int Next(int count)
        {
            Guard(count>0,"No legal option is available.");
            random^=random<<13; random^=random>>17; random^=random<<5;
            return (int)(random%(uint)count);
        }
        private T Pick<T>(IEnumerable<T> values)
        {
            var list=values.ToList(); return list[Next(list.Count)];
        }
        private static ScenarioStep Step(CommandKind kind,int seat=0,string value="",int target=-1,Hex? at=null,MoveMode mode=MoveMode.Secondary) =>
            new ScenarioStep { Command=kind.ToString(),Actor="p"+(seat+1),Value=value,Target=target<0 ? "none" : "p"+(target+1),Destination=at,MoveMode=mode.ToString() };
        public void Generate()
        {
            try
            {
                while(Definition.Steps.Count<limit && session.View(null).Phase!=Phase.Finished)
                {
                    beforeSave=session.ExportSave();
                    var chosen=Choose();
                    chosen.Name=$"{Definition.Steps.Count+1}: {chosen.Actor} {chosen.Command}";
                    Definition.Steps.Add(chosen);
                    var command=new Command
                    {
                        Id=Definition.Id+":"+Definition.Steps.Count,MatchId="scenario:"+Definition.Id,
                        ExpectedRevision=session.View(null).Revision,ActorSeat=int.Parse(chosen.Actor.Substring(1))-1,
                        Kind=(CommandKind)Enum.Parse(typeof(CommandKind),chosen.Command),Value=chosen.Value,
                        TargetSeat=chosen.Target=="none" ? -1 : int.Parse(chosen.Target.Substring(1))-1,
                        Destination=chosen.Destination??default,MoveMode=(MoveMode)Enum.Parse(typeof(MoveMode),chosen.MoveMode)
                    };
                    Report.LastAttempt=command;
                    var result=session.Execute(command.ActorSeat,command);
                    Guard(result.Accepted && !result.Duplicate,"Legal-choice command rejected: "+result.Code+" "+result.Message);
                    FinalSave=session.ExportSave(); var state=codec.Read(FinalSave);
                    CheckInvariants(catalog,state); CheckProjection(session.View(null));
                    chosen.Expect=Snapshot(state);
                    Report.Steps=Definition.Steps.Count;
                    bool firstChoice=state.Pending!=null && !Report.ChoicesSeen.Contains(state.Pending.Kind);
                    if(firstChoice) Report.ChoicesSeen.Add(state.Pending!.Kind);
                    if(Report.Steps%25==0 || firstChoice) Restore();
                    if(Report.Steps%17==0)
                    {
                        Guard(session.Execute((command.ActorSeat+1)%4,command).Code=="unauthorized","A different identity submitted another player's command.");
                        Guard(session.ExportSave()==FinalSave,"Unauthorized probe changed the save."); Report.UnauthorizedChecks++;
                    }
                    if(Report.Steps%25==0)
                    {
                        Guard(session.Execute(command.ActorSeat,command).Duplicate,"Accepted command did not remain idempotent after restore.");
                        Guard(session.ExportSave()==FinalSave,"Duplicate probe changed the save."); Report.DuplicateChecks++;
                        WriteArtifacts();
                    }
                }
                FinalSave=session.ExportSave(); Restore();
                Report.StopReason=session.View(null).Phase==Phase.Finished ? "match_finished" : "step_limit";
                Report.Passed=true;
            }
            catch(Exception error)
            {
                Report.StopReason="failure"; Report.Failure=error.ToString();
                FinalSave=session.ExportSave();
            }
            finally { WriteArtifacts(); }
        }
        private void Restore()
        {
            var restored=LocalGameFactory.Restore(catalog,FinalSave);
            Guard(restored.ExportSave()==FinalSave,"Restore changed the final save."); session=restored;
            if(!Report.RestoreCheckpoints.Contains(Report.Steps)) Report.RestoreCheckpoints.Add(Report.Steps);
        }
        internal void RecordScenarioReplay(bool passed,string errors)
        {
            Report.ScenarioReplayPassed=passed;
            if(!passed) { Report.Passed=false; Report.StopReason="failure"; Report.Failure="Exported scenario replay: "+errors; }
            WriteArtifacts();
        }
        private ScenarioStep Choose()
        {
            var view=session.View(null);
            if(view.Phase==Phase.HeroSelection)
            {
                int seat=Pick(view.Players.Where(p=>p.HeroId==null).Select(p=>p.Seat));
                return Step(CommandKind.ChooseHero,seat,Pick(session.View(seat).AvailableHeroes.OrderBy(id=>id,StringComparer.Ordinal)));
            }
            if(view.Phase==Phase.Deployment)
            {
                var choices=Enumerable.Range(0,4).SelectMany(actor=>session.View(actor).Deployments.SelectMany(pair=>pair.Value.Select(at=>Step(CommandKind.DeployHero,actor,target:pair.Key,at:at))));
                return Pick(choices);
            }
            if(view.Sandbox && !sandboxPrepared && view.Phase==Phase.Planning)
            {
                sandboxPrepared=true; PrepareSandbox(view);
            }
            if(setup.Count>0) return setup.Dequeue();
            if(view.Pending!=null)
            {
                int seat=view.Pending.ChooserSeat; var own=session.View(seat);
                switch(view.Pending.Kind)
                {
                    case "initiative": return Step(CommandKind.ChooseInitiative,seat,target:Pick(view.Pending.CandidateSeats));
                    case "hero_respawn": return Step(CommandKind.RespawnHero,seat,at:Pick(own.RespawnCells));
                    case "attack_target": return Step(CommandKind.ChooseAttackTarget,seat,Pick(own.AttackTargets));
                    case "primary_option": return Step(CommandKind.ChoosePrimaryOption,seat,Pick(own.PrimaryOptions));
                    case "effect_target": return Step(CommandKind.ChooseEffectTarget,seat,Pick(own.EffectTargets));
                    case "defense":
                        var successful=own.DefenseOptions.Where(d=>d.Assessment.Successful).ToList();
                        var defenses=successful.Count>0 && Next(4)!=0 ? successful : own.DefenseOptions;
                        return defenses.Count>0 && Next(5)!=0 ? Step(CommandKind.Defend,seat,Pick(defenses).CardId) : Step(CommandKind.DeclineDefense,seat);
                    case "forced_discard": return Step(CommandKind.ForcedDiscard,seat,Pick(own.ForcedDiscardCards));
                    case "optional_discard": return Step(CommandKind.ChooseOptionalDiscard,seat,Next(3)==0 ? "skip" : Pick(own.OptionalDiscardCards));
                    case "effect_move": return view.Pending.Optional && Next(3)==0 ? Step(CommandKind.ChooseEffectMove,seat,"skip") : Step(CommandKind.ChooseEffectMove,seat,at:Pick(own.EffectMoves).Destination);
                    case "placement": return Step(CommandKind.ChoosePlacement,seat,at:Pick(own.Placements));
                    case "minion_return":
                        var returning=Pick(own.MinionReturns);return Step(CommandKind.ChooseMinionReturn,seat,returning.UnitId,at:returning.Destination);
                    case "gold_transfer":
                        if(Next(3)==0)return Step(CommandKind.ChooseGoldTransfer,seat,"0");
                        var gold=Pick(own.GoldTransfers);return Step(CommandKind.ChooseGoldTransfer,seat,gold.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),target:gold.TargetSeat);
                    case "effect_minion": return Step(CommandKind.ChooseEffectTarget,seat,Next(3)==0 ? "skip" : Pick(own.EffectTargets));
                    case "card_swap": return Step(CommandKind.ChooseCardSwap,seat,Next(3)==0 ? "skip" : Pick(own.CardSwapOptions));
                    case "recover_discard": return Step(CommandKind.ChooseRecoveredCard,seat,Next(3)==0 ? "skip" : Pick(own.RecoverableCards));
                    case "round_minion_removal": return Step(CommandKind.ChooseRoundMinionRemoval,seat,Pick(own.RoundMinionRemovals));
                    case "minion_spawn":
                        return Pick(own.SpawnChoices.SelectMany(pair=>pair.Value.Select(at=>Step(CommandKind.ChooseMinionSpawn,seat,pair.Key,at:at))));
                    default: throw new InvalidOperationException("Simulation needs an explicit handler for choice: "+view.Pending.Kind);
                }
            }
            if(view.Phase==Phase.Planning)
            {
                var candidates=view.Players.Where(p=>!p.Confirmed).Select(p=>session.View(p.Seat)).ToList();
                var unselected=candidates.Where(v=>!v.OwnCards.Any(c=>c.Zone==CardZone.Selected)).ToList();
                if(unselected.Count>0)
                {
                    var own=Pick(unselected); int seat=own.Players.Single(p=>p.HeroId==catalog.Card(own.OwnCards[0].CardId).HeroId).Seat;
                    var hand=own.OwnCards.Where(c=>c.Zone==CardZone.InHand).ToList();
                    var primary=hand.Where(c=>own.SupportedPrimaryCards.Contains(c.CardId)).ToList();
                    return Step(CommandKind.SelectCard,seat,Pick(primary.Count>0 && Next(3)!=0 ? primary : hand).CardId);
                }
                Guard(!view.QuickSelection,"Quick selection is stuck after all available players selected.");
                return Step(CommandKind.ConfirmCard,Pick(view.Players.Where(p=>!p.Confirmed).Select(p=>p.Seat)));
            }
            if(view.Phase==Phase.RoundEnd)
            {
                if(view.UpgradingSeats.Count>0)
                {
                    int seat=Pick(view.UpgradingSeats); var own=session.View(seat);
                    var supported=own.UpgradeOptions.Where(o=>own.SupportedPrimaryCards.Contains(o.CardId) || own.SupportedDefenseCards.Contains(o.CardId)).ToList();
                    return Step(CommandKind.ChooseUpgrade,seat,Pick(supported.Count>0 && Next(4)!=0 ? supported : own.UpgradeOptions).CardId);
                }
                Guard(session.View(0).CanResolveRoundEnd,"Round end has no legal continuation.");
                return Step(CommandKind.ResolveRoundEnd);
            }
            if(view.Phase==Phase.Action && view.ActiveSeat.HasValue)
            {
                int seat=view.ActiveSeat.Value; var own=session.View(seat);
                var active=own.OwnCards.Single(c=>c.Zone==CardZone.PlayedUnresolved);
                bool attack=catalog.Card(active.CardId).PrimaryFamily=="attack";
                // An attack can gain targets after its optional movement or discard step.
                if(own.CanBeginPrimary && (!attack || own.AttackTargets.Count>0 || Next(5)==0) && Next(5)!=0) return Step(CommandKind.BeginPrimary,seat);
                if(!own.PrimarySupported && !Report.UnimplementedPrimaryCards.Contains(active.CardId)) Report.UnimplementedPrimaryCards.Add(active.CardId);
                var moves=own.SecondaryMoves.Select(m=>(option:m,mode:MoveMode.Secondary)).Concat(own.FastMoves.Select(m=>(option:m,mode:MoveMode.Fast))).ToList();
                if(moves.Count>0 && Next(7)!=0)
                {
                    var enemies=view.Units.Where(u=>u.Team!=view.Players[seat].Team).ToList();
                    int Score(MoveOption move) => enemies.Count==0 ? 0 : enemies.Min(u=>u.Position.Distance(move.Destination));
                    var best=moves.OrderBy(m=>Score(m.option)).ThenBy(m=>m.option.Destination.X).ThenBy(m=>m.option.Destination.Y).Take(5).ToList();
                    var chosen=Pick(Next(5)==0 ? moves : best);
                    return Step(CommandKind.Move,seat,at:chosen.option.Destination,mode:chosen.mode);
                }
                Guard(own.CanPass,"Active action has no legal continuation."); return Step(CommandKind.Pass,seat);
            }
            throw new InvalidOperationException("Simulation has no handler for phase "+view.Phase);
        }
        private void PrepareSandbox(GameView view)
        {
            setup.Enqueue(Step(CommandKind.DebugSetCrystal,value:"36",target:0));
            setup.Enqueue(Step(CommandKind.DebugSetCrystal,value:"36",target:1));
            var occupied=new HashSet<Hex>(view.Units.Select(u=>u.Position));
            foreach(var player in view.Players)
            {
                setup.Enqueue(Step(CommandKind.DebugSetGold,value:(6+Next(7)).ToString(System.Globalization.CultureInfo.InvariantCulture),target:player.Seat));
                var supported=catalog.Cards.Where(c=>c.HeroId==player.HeroId && c.Level>=2 && c.Color!="purple" && (view.SupportedPrimaryCards.Contains(c.Id) || view.SupportedDefenseCards.Contains(c.Id))).GroupBy(c=>c.Color).OrderBy(g=>g.Key,StringComparer.Ordinal);
                foreach(var group in supported) setup.Enqueue(Step(CommandKind.DebugEquipCard,value:Pick(group.OrderBy(c=>c.Id,StringComparer.Ordinal)).Id,target:player.Seat));
                var cells=catalog.Cells.Where(c=>!c.Obstacle && c.Region=="mid" && !occupied.Contains(c.Position)).OrderBy(c=>c.Position.Distance(new Hex(0,0))).ThenBy(c=>c.Position.X).ThenBy(c=>c.Position.Y).Take(8).ToList();
                var at=Pick(cells).Position; occupied.Add(at);
                setup.Enqueue(Step(CommandKind.DebugTeleport,value:"hero:"+player.Seat,at:at));
            }
        }
        private static ScenarioExpectation Snapshot(GameState state)
        {
            var expect=new ScenarioExpectation
            {
                Phase=state.Phase.ToString(),Round=state.Round,Turn=state.Turn,
                Active=state.ActiveSeat.HasValue ? "p"+(state.ActiveSeat.Value+1) : "none",
                Winner=state.Winner?.ToString()??"none",CombatRegion=state.CombatRegion,
                BlueCrystal=state.BlueCrystal,RedCrystal=state.RedCrystal,BlueMarks=state.BlueMarks,RedMarks=state.RedMarks,
                RoundEndStage=state.RoundEnd?.Stage??"none",UpgradingPlayers=state.RoundEnd?.Upgrades.Count(p=>p.PendingLevels.Count>0)??0,
                PendingKind=state.Pending?.Kind??"none",PendingChooser=state.Pending==null ? "none" : "p"+(state.Pending.ChooserSeat+1)
            };
            foreach(var player in state.Players)
            {
                string key="p"+(player.Seat+1); expect.Gold[key]=player.Gold; expect.Levels[key]=player.Level;
                expect.HandCounts[key]=player.Cards.Count(c=>c.Zone==CardZone.InHand || c.Zone==CardZone.Selected);
                expect.DiscardCounts[key]=player.Cards.Count(c=>c.Zone==CardZone.Discarded);
                expect.UpgradeCounts[key]=player.UpgradeHistory.Count;
            }
            return expect;
        }
        private static void Guard(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
        internal static void CheckInvariants(ContentCatalog catalog,GameState state)
        {
            Guard(state.Units.Select(u=>u.Id).Distinct().Count()==state.Units.Count,"Duplicate unit ID.");
            Guard(state.Units.Select(u=>u.Position).Distinct().Count()==state.Units.Count,"Units overlap.");
            Guard(state.Units.All(u=>catalog.Cell(u.Position)?.Obstacle==false),"Unit lies outside playable map.");
            Guard(state.Players.Count==4 && state.Players.Select(p=>p.Seat).SequenceEqual(new[]{0,1,2,3}),"Invalid seats.");
            Guard(state.Round>=1 && state.Turn>=1 && state.Turn<=catalog.Rules.TurnsPerRound,"Invalid round/turn.");
            Guard(state.Revision==state.AcceptedCommands.Count && state.Revision==state.Receipts.Count,"Journal/receipt/revision mismatch.");
            Guard(state.Receipts.Select(r=>r.Id).Distinct().Count()==state.Receipts.Count,"Duplicate receipt.");
            Guard(state.Events.Select((e,i)=>e.Sequence==i+1L && e.Revision>0 && e.Revision<=state.Revision).All(ok=>ok),"Invalid event sequence/revision.");
            Guard((state.Phase==Phase.Finished)==state.Winner.HasValue,"Winner/phase mismatch.");
            Guard(state.Phase!=Phase.Finished || state.Pending==null && state.Execution==null && !state.ActiveSeat.HasValue && state.Frontline==null && state.RoundEnd==null,"Finished match retains an active continuation.");
            Guard(state.Phase!=Phase.EffectChoice || state.Pending!=null,"Effect choice has no pending choice.");
            foreach(var player in state.Players)
            {
                Guard(player.Level>=1 && player.Level<=8 && player.Gold>=0,"Invalid player resources.");
                if(player.HeroId==null) continue;
                Guard(player.Cards.Count==catalog.Rules.HandSize && player.Cards.Select(c=>catalog.Card(c.CardId).Color).Distinct().Count()==catalog.Rules.HandSize,"Player card colors are not conserved.");
                Guard(player.Cards.All(c=>catalog.Card(c.CardId).HeroId==player.HeroId && catalog.Card(c.CardId).Color!="purple"),"Card does not belong in this player's five-card deck.");
                Guard(player.Cards.Count(c=>c.Zone==CardZone.Selected)<=1 && player.Cards.Count(c=>c.Zone==CardZone.PlayedUnresolved)<=1,"Player has multiple unresolved selections/actions.");
                Guard(player.UpgradeHistory.Count<=6,"Too many color upgrades.");
                if(state.Phase!=Phase.HeroSelection && state.Phase!=Phase.Deployment)
                    Guard(state.Units.Count(u=>u.Kind=="hero" && u.Seat==player.Seat)==(player.AwaitingRespawn ? 0 : 1),"Hero presence disagrees with respawn state.");
            }
        }
        private static void CheckProjection(GameView view)
        {
            Guard(view.OwnCards.Count==0 && view.DefenseOptions.Count==0 && view.ForcedDiscardCards.Count==0 && view.OptionalDiscardCards.Count==0,"Public view exposes private choices.");
            Guard(view.Events.All(e=>e.PrivateTo==null),"Public view exposes private events.");
            Guard(view.Pending?.SourcePrivateTo==null || view.Pending.Source=="","Public view exposes private pending source.");
            Guard(view.Effects.All(e=>!e.SourcePrivateTo.HasValue || e.SourceCardId==""),"Public view exposes private effect source.");
        }
        private void WriteArtifacts()
        {
            if(FinalSave=="") FinalSave=session.ExportSave();
            var state=codec.Read(FinalSave); Report.FinalRound=state.Round;
            Report.Commands=state.AcceptedCommands.GroupBy(c=>c.Kind.ToString()).ToDictionary(g=>g.Key,g=>g.Count());
            Report.Events=state.Events.GroupBy(e=>e.Kind).ToDictionary(g=>g.Key,g=>g.Count());
            using(var hash=SHA256.Create()) Report.FinalStateHash=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(FinalSave))).Replace("-","").ToLowerInvariant();
            if(output==null) return;
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,Definition.Id+".json"),JsonConvert.SerializeObject(Definition,Formatting.Indented),new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output,"report.json"),JsonConvert.SerializeObject(Report,Formatting.Indented),new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output,"final-save.json"),FinalSave,new UTF8Encoding(false));
            if(Report.StopReason=="failure") File.WriteAllText(Path.Combine(output,"before-failure.json"),beforeSave,new UTF8Encoding(false));
        }
    }
}

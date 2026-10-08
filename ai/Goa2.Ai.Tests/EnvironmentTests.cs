using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Goa2.Ai.Tests
{
    public sealed class EnvironmentTests
    {
        private static string Root
        {
            get { var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory); while(d!=null && !File.Exists(Path.Combine(d.FullName,"content","manifest.json"))) d=d.Parent; return d?.FullName??throw new Exception("root"); }
        }
        private static ContentCatalog Catalog()=>ContentLoader.LoadDirectory(Root);
        private static string Json(object x)=>JsonConvert.SerializeObject(x);
        private static GameSession Ready()
        {
            var c=Catalog(); var s=LocalGameFactory.Create(c,"test",new[]{"a","b","c","d"},42,true);
            var result=s.Execute(0,new Command { Id="setup", MatchId="test", Kind=CommandKind.DebugPrepare, Value="wasp,shargatha,brogan,arien" });
            Assert.That(result.Accepted,Is.True); return s;
        }
        [TestCase(1)] [TestCase(2)]
        public void OtherAndTeammateHiddenSelectionsHaveIdenticalObservationsAndCandidates(int hiddenSeat)
        {
            var catalog=Catalog(); var codec=new JsonStateCodec(); var origin=codec.Read(Ready().ExportSave());
            origin.Sandbox=false; origin.QuickSelection=false;
            string? previous=null;
            foreach(var card in origin.Players[hiddenSeat].Cards)
            {
                var state=codec.Read(codec.Write(origin)); state.Players[hiddenSeat].Cards.Single(c=>c.CardId==card.CardId).Zone=CardZone.Selected;
                state.Events.Add(new GameEvent { Kind="CardSelected", Seat=hiddenSeat, PrivateTo=hiddenSeat, CardId=card.CardId });
                var session=new GameSession(catalog,codec,state); var env=new HeadlessEnvironment(catalog,session,42,"privacy");
                var decision=env.Next()!; Assert.That(decision.Observation.Seat,Is.Zero);
                var payload=Json(decision); if(previous!=null) Assert.That(payload,Is.EqualTo(previous)); previous=payload;
                Assert.That(decision.Observation.Players[hiddenSeat].Cards.All(c=>c.Zone=="InHand"),Is.True);
                Assert.That(payload,Does.Not.Contain("Debug").And.Not.Contain("Random").And.Not.Contain("ResumeAt").And.Not.Contain("PrivateTo"));
            }
        }
        [Test]
        public void InputsAreDetachedAndStaleOrUnknownActionsDoNotModifyAuthority()
        {
            var env=new HeadlessEnvironment(Catalog(),42,"isolation"); var d=env.Next()!; string before=env.ExportSave();
            d.Actions.Clear(); d.Observation.Players.Clear();
            Assert.That(env.Next()!.Actions,Is.Not.Empty);
            Assert.Throws<InvalidOperationException>(()=>env.Submit(-1,"bad"));
            Assert.Throws<InvalidOperationException>(()=>env.Submit(env.Next()!.Revision,"bad"));
            Assert.That(env.ExportSave(),Is.EqualTo(before));
        }
        [Test]
        public void FormalCoinAndPolicyStreamsAreDeterministicAndCoinIsNotAnAction()
        {
            string Run()
            {
                var c=Catalog(); var env=new HeadlessEnvironment(c,71,"determinism"); var policies=Enumerable.Range(0,4).Select(s=>new RandomPolicy(17+s)).ToArray();
                for(int i=0;i<85;i++)
                {
                    var d=env.Next()!; Assert.That(d.Actions.Any(a=>a.Kind.Contains("Coin")),Is.False);
                    env.Submit(d.Revision,policies[d.Observation.Seat].Choose(d.Observation,d.Actions));
                }
                return env.ExportSave();
            }
            var a=Run(); Assert.That(Run(),Is.EqualTo(a)); Assert.That(LocalGameFactory.Restore(Catalog(),a).ExportSave(),Is.EqualTo(a));
        }
        [Test]
        public void TrueVictoryIsDistinctFromAnExternalStepLimit()
        {
            var c=Catalog(); var session=Ready(); var codec=new JsonStateCodec(); var state=codec.Read(session.ExportSave());
            var env=new HeadlessEnvironment(c,session,1,"live"); Assert.That(env.Terminated,Is.False);
            state.Phase=Phase.Finished; state.Winner=Team.Red;
            var finished=new HeadlessEnvironment(c,new GameSession(c,codec,state),1,"finished");
            Assert.That(finished.Next(),Is.Null); Assert.That(finished.Terminated,Is.True);
        }
        [Test]
        public void UnknownPendingWindowIsAnExplicitFailure()
        {
            var c=Catalog(); var codec=new JsonStateCodec(); var state=codec.Read(Ready().ExportSave());
            state.Pending=new PendingChoice { Kind="future_mechanic", ChooserSeat=2 };
            var env=new HeadlessEnvironment(c,new GameSession(c,codec,state),1,"unknown");
            Assert.That(Assert.Throws<InvalidOperationException>(()=>env.Next())!.Message,Does.Contain("unsupported_decision:future_mechanic"));
        }
        [Test]
        public void PublicProfilesAreExplicitImmutableAndRestoreUnderMatchingContentOnly()
        {
            var original=Catalog(); var alternate=ProfileCatalog.Load(Root,10,4);
            Assert.That(alternate.Hash,Is.Not.EqualTo(original.Hash));
            Assert.That(ProfileCatalog.Load(Root,10,4).Hash,Is.EqualTo(alternate.Hash));
            var env=new HeadlessEnvironment(alternate,7,"profile"); var d=env.Next()!;
            Assert.That(d.Observation.Schema,Is.EqualTo(5));
            Assert.That(d.Observation.Rules.StartingCrystalLife,Is.EqualTo(10));
            Assert.That(d.Observation.Rules.VictoryMarksRequired,Is.EqualTo(4));
            Assert.That(d.Observation.BlueCrystal,Is.EqualTo(10));
            d.Observation.Rules.StartingCrystalLife=100;
            Assert.That(env.Next()!.Observation.Rules.StartingCrystalLife,Is.EqualTo(10));
            var save=env.ExportSave(); Assert.That(LocalGameFactory.Restore(alternate,save).ExportSave(),Is.EqualTo(save));
            Assert.Throws<RuleViolation>(()=>LocalGameFactory.Restore(original,save));
            Assert.Throws<InvalidDataException>(()=>ArtifactContract.Current(original).RequireCompatible(ArtifactContract.Current(alternate)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>ProfileCatalog.Load(Root,0,4));
            Assert.That(Catalog().Hash,Is.EqualTo(original.Hash));
        }
        [Test]
        public void IncompatibleDataRulesAndFormatsAreRejected()
        {
            var expected=ArtifactContract.Current(Catalog());
            foreach(string field in new[]{"ObservationVersion","ActionVersion","EngineVersion","ContentHash","RulesVersion","ContentVersion"})
            {
                var altered=JsonConvert.DeserializeObject<ArtifactContract>(Json(expected))!;
                var f=typeof(ArtifactContract).GetField(field)!;
                f.SetValue(altered,f.FieldType==typeof(int)?(object)999:"different");
                Assert.Throws<InvalidDataException>(()=>altered.RequireCompatible(expected));
            }
            expected.RequireCompatible(ArtifactContract.Current(Catalog()));
        }
        [Test]
        public void PublicBoundaryInventoryRequiresReviewOfEveryNewCoreField()
        {
            var inventory=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(Root,"ai","observation-coverage.json")));
            Assert.That((bool)inventory["complete"]!,Is.False,"Known semantic gaps must not be called complete.");
            foreach(var type in new[]{typeof(GameView),typeof(PlayerView),typeof(GameEvent),typeof(PendingChoice),typeof(CardDefinition),typeof(AttackBreakdown),typeof(ActiveEffect),typeof(EffectWindow),typeof(RuleSettings),typeof(UnitState),typeof(CardInstance),typeof(CellDefinition),typeof(DefenseAssessment),typeof(ActionSequenceView),typeof(ActionCardView)})
            {
                var fields=((Newtonsoft.Json.Linq.JObject)inventory["types"]![type.Name]!).Properties().Select(p=>p.Name).ToArray();
                Assert.That(fields,Is.EquivalentTo(type.GetFields().Select(f=>f.Name)),type.Name+": classify new fields before training");
            }
        }
        [Test]
        public void AtomicProjectionPreservesPerOwnerCardsTimesBonusesAndEffectRelations()
        {
            var catalog=Catalog();var own=Ready().View(0);var ids=new StableIds(catalog);
            own.Players[0].Gold=3;own.Players[2].Gold=7;
            own.OwnCards[0].Zone=CardZone.Selected;own.OwnCards[0].PlayedRound=2;own.OwnCards[0].PlayedTurn=3;
            own.Players[1].PurpleCardId=catalog.Cards.First(c=>c.PrimaryFamily=="ultimate").Id;
            own.Players[1].PoisonIncludesDefense=true;
            own.Players[1].PermanentBonuses["攻击"]=2;own.Players[1].EffectiveBonuses!["攻击"]=-2;
            var effect=new ActiveEffect {Id="opaque-private-origin",SourceCardId="",SourceUnitId="hero:1",ProtectedUnitId="hero:2",ControllerSeat=1,ExemptControllerSeat=2,Kind=EffectKind.MovementBoundary,Duration=EffectDuration.NextTurn,AreaKind=EffectAreaKind.SkillRange,BaseRadius=3,PersistsThroughDefeat=true,CreatedRound=2,CreatedTurn=2,Window=new EffectWindow {StartRound=2,StartTurn=3,EndRound=2,EndTurn=3}};
            own.Effects.Add(effect);own.EffectAreas[effect.Id]=new List<Hex>{new Hex(0,0),new Hex(1,0)};
            var o=ObservationProjector.Project(own,0,ids,PublicRuleProfile.From(catalog));
            Assert.That(o.Players[0].Gold,Is.EqualTo(3));Assert.That(o.Players[2].Gold,Is.EqualTo(7));
            Assert.That(o.Players[0].Cards[0].Zone,Is.EqualTo("Selected"));
            Assert.That(o.Players[0].Cards[0].PlayedRound,Is.EqualTo(2));Assert.That(o.Players[0].Cards[0].PlayedTurn,Is.EqualTo(3));
            Assert.That(o.Players[1].Effective.Attack,Is.EqualTo(-2));Assert.That(o.Players[1].Permanent.Attack,Is.EqualTo(2));
            Assert.That(o.Players[1].PoisonDefense,Is.True);Assert.That(o.Players[1].Purple,Is.Not.Empty);
            var e=o.Effects.Single();Assert.That(e.ExemptSeat,Is.EqualTo(2));Assert.That(e.Area.Count,Is.EqualTo(2));Assert.That(e.PersistsThroughDefeat,Is.True);
            Assert.That(Json(o),Does.Not.Contain("opaque-private-origin").And.Not.Contain("HandCount").And.Not.Contain("OwnCards"));
            own.Players[1].EffectiveBonuses!["future_stat"]=1;
            Assert.Throws<InvalidOperationException>(()=>ObservationProjector.Project(own,0,ids,PublicRuleProfile.From(catalog)));
        }
        [Test]
        public void PublicEventTimesAndReferencesDoNotExposePrivateEventSequenceGaps()
        {
            var c=Catalog();var own=Ready().View(0);
            own.Events=new List<GameEvent> {
                new GameEvent {Kind="PlanningStarted",Sequence=1,Detail="3:2"},
                new GameEvent {Kind="CardSelected",Sequence=2,PrivateTo=1,CardId="must-not-leak"},
                new GameEvent {Kind="MinionRemoved",Sequence=90,Detail="test-minion"},
                new GameEvent {Kind="GoldTransferred",Sequence=92,Seat=0,Detail="target:2|amount:3"} };
            var o=ObservationProjector.Project(own,0,new StableIds(c),PublicRuleProfile.From(c));
            Assert.That(o.PublicHistory.Select(e=>e.Ordinal),Is.EqualTo(new[]{0,1,2}));
            Assert.That(o.PublicHistory[1].Round,Is.EqualTo(3));Assert.That(o.PublicHistory[1].Turn,Is.EqualTo(2));
            Assert.That(o.PublicHistory[1].Unit,Is.EqualTo("test-minion"));Assert.That(o.PublicHistory[2].OtherSeat,Is.EqualTo(2));
            Assert.That(o.PublicHistory[2].Amount,Is.EqualTo(3));Assert.That(Json(o),Does.Not.Contain("must-not-leak"));
        }
        [Test]
        public void ResponseWindowsFromRealScenarioPrefixesExposeOnlyAcceptedCandidates()
        {
            var c=Catalog(); var codec=new JsonStateCodec(); var checkedWindows=new HashSet<string>(); var coverage=new Dictionary<string,int>();
            var examples=new List<string>();
            string[] names={"cloak-repeat","cloak-move","advantage-return","blink-shadowstep","throwing-axe-reflection","lord-tides","counterattack","fortify","loyal-recover","wall-recover","master-two","tidal-wave-two","defensive-counter","spawn-order","round-frontline"};
            var paths=Directory.GetFiles(Path.Combine(Root,"tests","scenarios"),"*.json").Where(p=>names.Any(n=>Path.GetFileNameWithoutExtension(p).Contains(n,StringComparison.Ordinal))).ToList();
            foreach(var path in paths)
            {
                var definition=ScenarioRunner.Load(File.ReadAllText(path)); definition.VerifyReplayAfterEachStep=false;
                var runner=new ScenarioRunner(c,definition);
                while(!runner.Complete)
                {
                    var step=runner.Next(); Assert.That(step.Passed,Is.True,path+":"+step.Number+":"+string.Join(";",step.Errors));
                    var state=codec.Read(runner.Session.ExportSave()); if(state.Pending==null) continue;
                    string window=state.Pending.Kind+"/"+state.Pending.ResumeAt;
                    if(!checkedWindows.Add(window)) continue;
                    var env=new HeadlessEnvironment(c,new GameSession(c,codec,state),1,"probe"); var d=env.Next()!;
                    Assert.That(d.Observation.Seat,Is.EqualTo(state.Pending.ChooserSeat));
                    var own=new GameSession(c,codec,state).View(d.Observation.Seat);
                    if(own.Attack!=null)
                    {
                        Assert.That(d.Observation.Attack!.Final,Is.EqualTo(own.Attack.FinalAttack));
                        Assert.That(d.Observation.Attack.Defender,Is.EqualTo(own.Attack.DefenderSeat));
                        Assert.That(d.Observation.Attack.Card,Is.EqualTo(new StableIds(c).Card(own.Attack.SourceCardId)));
                    }
                    var teacher=new SimplePolicy(c,19);
                    Assert.That(teacher.Preferred(d.Observation,d.Actions),Does.Contain(teacher.Choose(d.Observation,d.Actions)));
                    coverage[window]=d.Actions.Count;
                    examples.Add(Json(new {Window=window,Decision=d}));
                    foreach(var candidate in d.Actions)
                    {
                        var fresh=new HeadlessEnvironment(c,new GameSession(c,codec,state),1,"probe"); var fd=fresh.Next()!;
                        Assert.DoesNotThrow(()=>fresh.Submit(fd.Revision,candidate.Id),path+" "+window+" "+candidate.Id);
                    }
                }
            }
            TestContext.WriteLine(Json(coverage)); Assert.That(checkedWindows.Count,Is.GreaterThanOrEqualTo(25));
            File.WriteAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory,"response-v5.jsonl"),examples);
            foreach(var kind in new[]{"discard_attack/","action_minion_removal/","forced_discard/","recover_discard/","gold_transfer/","minion_protection/","minion_spawn/","round_minion_removal/"})
                Assert.That(checkedWindows.Any(w=>w.StartsWith(kind,StringComparison.Ordinal)),Is.True,"missing coverage: "+kind);
        }
    }
}

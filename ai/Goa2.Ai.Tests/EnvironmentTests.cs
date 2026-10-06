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
        public void ResponseWindowsFromRealScenarioPrefixesExposeOnlyAcceptedCandidates()
        {
            var c=Catalog(); var codec=new JsonStateCodec(); var checkedWindows=new HashSet<string>(); var coverage=new Dictionary<string,int>();
            string[] names={"cloak-repeat","cloak-move","advantage-return","blink-shadowstep","throwing-axe-reflection","lord-tides","counterattack","fortify","loyal-recover","wall-recover","master-two","tidal-wave-two","defensive-counter"};
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
                    coverage[window]=d.Actions.Count;
                    foreach(var candidate in d.Actions)
                    {
                        var fresh=new HeadlessEnvironment(c,new GameSession(c,codec,state),1,"probe"); var fd=fresh.Next()!;
                        Assert.DoesNotThrow(()=>fresh.Submit(fd.Revision,candidate.Id),path+" "+window+" "+candidate.Id);
                    }
                }
            }
            TestContext.WriteLine(Json(coverage)); Assert.That(checkedWindows.Count,Is.GreaterThanOrEqualTo(25));
            foreach(var kind in new[]{"discard_attack/","action_minion_removal/","forced_discard/","recover_discard/","gold_transfer/","minion_protection/"})
                Assert.That(checkedWindows.Any(w=>w.StartsWith(kind,StringComparison.Ordinal)),Is.True,"missing coverage: "+kind);
        }
    }
}

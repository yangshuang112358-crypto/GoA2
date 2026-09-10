using System;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure.Scenarios;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class ScenarioTests
    {
        [Test]
        public void AttackAssertionsCannotTreatAMissingAttackAsZero()
        {
            var step=Step("DebugPrepare"); step.Expect.AttackBase=0; step.Expect.AttackBonus=0; step.Expect.CardTextBonus=0; step.Expect.AttackFinal=0;
            var runner=Run(Definition(step));
            Assert.That(runner.Report.Passed, Is.False); Assert.That(runner.Report.Steps.Single().Errors.Count, Is.EqualTo(4));
        }
        [Test]
        public void AuraAssertionsRejectWrongSourcesAndRestrictions()
        {
            var step=Step("DebugPrepare");
            step.Expect.EffectCounts["wasp-06-静电封锁"]=1;
            step.Expect.PrimaryRestrictions["p1"]="arien-06-打断施法";
            var runner=Run(Definition(step));
            Assert.That(runner.Report.Passed, Is.False);
            Assert.That(runner.Report.Steps.Single().Errors.Count, Is.EqualTo(2));
        }
        [Test]
        public void UpgradeAssertionsCheckTheActualStateInsteadOfOnlyAcceptingTheCommand()
        {
            var definition = new Goa2.Infrastructure.Scenarios.ScenarioDefinition
            {
                SchemaVersion=1,Id="wrong-upgrade",Name="Incorrect upgrade expectations",
                Steps = new System.Collections.Generic.List<Goa2.Infrastructure.Scenarios.ScenarioStep>
                {
                    new Goa2.Infrastructure.Scenarios.ScenarioStep { Command="DebugPrepare",Value="wasp,shargatha,brogan,arien",
                        Expect = new Goa2.Infrastructure.Scenarios.ScenarioExpectation
                        {
                            Levels = new System.Collections.Generic.Dictionary<string,int> { ["p1"]=8 },
                            UpgradeCounts = new System.Collections.Generic.Dictionary<string,int> { ["p1"]=6 },
                            PurpleCards = new System.Collections.Generic.Dictionary<string,string> { ["p1"]="wasp-12-电闪雷鸣" },
                            RoundEndStage="upgrades",UpgradingPlayers=4,RemainingMinionRemovals=1
                        }
                    }
                }
            };
            var runner=new Goa2.Infrastructure.Scenarios.ScenarioRunner(BattlefieldTests.Catalog(),definition);
            var result=runner.Next();
            Assert.That(result.Accepted, Is.True); Assert.That(result.Passed, Is.False);
            Assert.That(result.Errors.Count, Is.EqualTo(6));
        }
        [TestCase("sandbox-smoke.json")]
        [TestCase("permissions.json")]
        [TestCase("formal-turn.json")]
        [TestCase("frontline.json")]
        [TestCase("spawn-order.json")]
        [TestCase("combat-defense.json")]
        [TestCase("round-upgrades.json")]
        [TestCase("round-frontline.json")]
        [TestCase("static-field.json")]
        [TestCase("skill-suppression.json")]
        [TestCase("shining-blade.json")]
        [TestCase("deflection-barrier.json")]
        [TestCase("reflection-barrier.json")]
        [TestCase("marksman.json")]
        [TestCase("headshot.json")]
        [TestCase("cleave.json")]
        [TestCase("deadly-sweep.json")]
        [TestCase("death-spin.json")]
        [TestCase("backstab.json")]
        public void PublishedScenarioPassesAgainstTheFormalCatalog(string file)
        {
            string root = ContentTests.Root();
            var definition = ScenarioRunner.Load(File.ReadAllText(Path.Combine(root,"tests","scenarios",file)));
            var runner = new ScenarioRunner(Goa2.Infrastructure.ContentLoader.LoadDirectory(root),definition);
            while (!runner.Complete) runner.Next();
            Assert.That(runner.Report.Passed, Is.True, string.Join("; ", runner.Report.Steps.SelectMany(s => s.Errors)));
        }
        private static ScenarioDefinition Definition(params ScenarioStep[] steps) => new ScenarioDefinition { SchemaVersion = 1, Id = "fixture", Name = "合同验收", Steps = steps.ToList() };
        private static ScenarioStep Step(string command, string value = "", string actor = "p1", string target = "none") => new ScenarioStep { Command = command, Value = value, Actor = actor, Target = target };
        private static ScenarioRunner Run(ScenarioDefinition definition)
        {
            var runner = new ScenarioRunner(FoundationTests.Fixture(), definition);
            while (!runner.Complete) runner.Next();
            return runner;
        }
        [Test]
        public void ScenarioIsDeterministicAndRejectsInvalidCommandsWithoutStateChanges()
        {
            var gold = Step("DebugGold", "10", target: "p2"); gold.Expect.Gold.Add("p2", 10);
            var duplicate = new ScenarioStep { ReplayStep = 2 }; duplicate.Expect.Code = "duplicate";
            var invalid = Step("DebugTeleport", "hero:0"); invalid.Destination = new Hex(99,99); invalid.Expect.Code = "invalid_teleport";
            var end = Step("DebugAdvance", "round"); end.Expect.Phase = "RoundEnd"; end.Expect.Revealed = 16;
            end.Expect.EventCounts.Add("ActionPassed", 16); end.Expect.EventCounts.Add("CardRevealed", 16);
            var input = Definition(Step("DebugPrepare"), gold, duplicate, invalid, end);
            var first = Run(input); var second = Run(input);
            Assert.That(first.Report.Passed, Is.True);
            Assert.That(first.Report.Steps.Count, Is.EqualTo(5));
            Assert.That(first.Report.Steps.All(s => s.Passed && s.Restored), Is.True);
            Assert.That(first.Report.Steps[1].StateHash, Is.EqualTo(first.Report.Steps[2].StateHash));
            Assert.That(first.Report.Steps[2].StateHash, Is.EqualTo(first.Report.Steps[3].StateHash));
            Assert.That(first.Report.FinalStateHash, Is.EqualTo(second.Report.FinalStateHash));
            Assert.That(first.Session.ExportSave(), Is.EqualTo(second.Session.ExportSave()));
        }
        [Test]
        public void ScenarioResolvesCurrentChooserAndResumesAfterSavingPendingChoice()
        {
            var reveal = Step("DebugSelectAll"); reveal.Expect.PendingKind = "initiative"; reveal.Expect.PendingChooser = "p1";
            var choose = Step("ChooseInitiative", actor: "chooser", target: "p3"); choose.Expect.Active = "p3";
            choose.Expect.EventOrder.AddRange(new[] { "InitiativeChosen", "ActionStarted" });
            var pass = Step("Pass", actor: "active"); pass.Expect.Active = "none"; pass.Expect.PendingChooser = "p2";
            var runner = Run(Definition(Step("DebugPrepare"), reveal, choose, pass));
            Assert.That(runner.Report.Passed, Is.True);
            Assert.That(runner.Report.Steps[2].Command!.ActorSeat, Is.EqualTo(0));
            Assert.That(runner.Report.Steps[2].Command!.TargetSeat, Is.EqualTo(2));
            Assert.That(runner.Report.Steps[3].Command!.ActorSeat, Is.EqualTo(2));
            Assert.That(runner.Session.View(null).Pending!.ChooserSeat, Is.EqualTo(1));
        }
        [Test]
        public void WrongExpectationStopsScenarioBeforeLaterCommands()
        {
            var wrong = Step("DebugPrepare"); wrong.Expect.Phase = "Action";
            var runner = Run(Definition(wrong, Step("DebugGold", "10", target: "p1")));
            Assert.That(runner.Report.Passed, Is.False);
            Assert.That(runner.Report.Steps.Count, Is.EqualTo(1));
            Assert.That(runner.Report.Steps[0].Errors.Any(e => e.Contains("Phase")), Is.True);
            Assert.That(runner.Session.View(null).Players[0].Gold, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => runner.Next());
        }
        [Test]
        public void ScenarioCanVerifyAuthenticationFailureAndExpectedPrivateDiscardCount()
        {
            var spoof = Step("DebugGold", "10", actor: "p2", target: "p1"); spoof.AuthenticateAs = "p1"; spoof.Expect.Code = "unauthorized";
            var discard = Step("DebugDiscard", "hero0-red", target: "p1"); discard.Expect.DiscardCounts.Add("p1", 1); discard.Expect.HandCounts.Add("p1", 4);
            discard.Expect.EventOrder.AddRange(new[] { "CardDiscarded", "DiscardColorShown" });
            var runner = Run(Definition(Step("DebugPrepare"), spoof, discard));
            Assert.That(runner.Report.Passed, Is.True);
            Assert.That(runner.Report.Steps[0].StateHash, Is.EqualTo(runner.Report.Steps[1].StateHash));
            Assert.That(runner.Session.View(null).Events.Any(e => e.CardId == "hero0-red"), Is.False);
        }
        [Test]
        public void ScenarioSchemaRejectsTyposDuplicatesEmptyStepsAndUnknownCommands()
        {
            const string valid = "{\"SchemaVersion\":1,\"Id\":\"smoke\",\"Name\":\"场景\",\"Steps\":[{\"Command\":\"DebugPrepare\"}]}";
            Assert.That(ScenarioRunner.Load(valid).Steps.Single().Command, Is.EqualTo("DebugPrepare"));
            foreach (string invalid in new[] {
                valid.Replace("SchemaVersion", "ScheemaVersion"),
                valid.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"SchemaVersion\":2"),
                valid.Replace("\"SchemaVersion\":1,", ""),
                valid.Replace("DebugPrepare", "DebugPrapare"),
                valid.Replace("[{\"Command\":\"DebugPrepare\"}]", "[]"),
                valid.Replace("smoke", "../outside"),
                valid.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"Seed\":true"),
                valid.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"Seed\":1.5"),
                valid.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"seed\":42"),
                valid.Replace("\"Command\":\"DebugPrepare\"", "\"Command\":\"DebugPrepare\",\"Destination\":{\"X\":1}"),
                valid.Replace("\"Command\":\"DebugPrepare\"", "\"Command\":\"DebugPrepare\",\"Destination\":{\"X\":1.5,\"Y\":2}"),
                valid.Replace("\"Command\":\"DebugPrepare\"", "\"Command\":\"DebugPrepare\",\"Expect\":{\"EffectCounts\":{\"card\":-1}}"),
                valid.Replace("\"Command\":\"DebugPrepare\"", "\"Command\":\"DebugPrepare\",\"Expect\":{\"EffectCounts\":null}"),
                valid.Replace("\"Command\":\"DebugPrepare\"", "\"Command\":\"DebugPrepare\",\"Expect\":{\"PrimaryRestrictions\":{\"p5\":\"none\"}}"),
                valid.Replace("\"Command\":\"DebugPrepare\"", "\"Command\":\"DebugPrepare\",\"Expect\":{\"PrimaryRestrictions\":{\"p1\":\"\"}}") })
                Assert.Throws<InvalidDataException>(() => ScenarioRunner.Load(invalid), invalid);
        }
    }
}

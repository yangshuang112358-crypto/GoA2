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
        [TestCase("sandbox-smoke.json")]
        [TestCase("permissions.json")]
        [TestCase("formal-turn.json")]
        [TestCase("frontline.json")]
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
                valid.Replace("\"Command\":\"DebugPrepare\"", "\"Command\":\"DebugPrepare\",\"Destination\":{\"X\":1.5,\"Y\":2}") })
                Assert.Throws<InvalidDataException>(() => ScenarioRunner.Load(invalid), invalid);
        }
    }
}

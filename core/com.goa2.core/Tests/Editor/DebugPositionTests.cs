#nullable enable
using System;
using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Infrastructure;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class DebugPositionTests
    {
        private static string Root => ContentTests.Root();
        private static string Prepared() => DebugPositions.Prepare(Root);
        private static string[] PositionIds() => JArray.Parse(File.ReadAllText(Path.Combine(Root,"tools","debug-positions.json"))).Select(p=>p["id"]!.Value<string>()!).ToArray();
        [TestCaseSource(nameof(PositionIds))]
        public void PreparedPositionsExecuteRealCommandsAndRestoreAtTheirAdvertisedChoice(string id)
        {
            var catalog=BattlefieldTests.Catalog(); var positions=DebugPositions.Read(Prepared());
            var position=positions.Single(p=>p.Id==id);
            var session=DebugPositions.Open(catalog,position); var view=session.View(null);
            Assert.That(view.Sandbox,Is.True); Assert.That(view.Revision,Is.GreaterThan(0));
            Assert.That(view.Phase,Is.EqualTo(position.Phase)); Assert.That(view.Pending?.Kind ?? "",Is.EqualTo(position.Pending));
            Assert.That(LocalGameFactory.Restore(catalog,session.ExportSave()).ExportSave(),Is.EqualTo(session.ExportSave()));
            if(id=="axe-reflection") Assert.That(view.Revision,Is.EqualTo(10),"Manual defense tests begin before the attack, not at the response.");
        }
        [Test]
        public void ManualCardPositionsStartBeforeTheirActionAndExplainHowToPlayThem()
        {
            var catalog=BattlefieldTests.Catalog();var metadata=JArray.Parse(File.ReadAllText(Path.Combine(Root,"tools","debug-positions.json")));
            foreach(var position in DebugPositions.Read(Prepared()).Where(p=>!new[]{"upgrades","respawn","occupied-spawn"}.Contains(p.Id)))
            {
                var session=DebugPositions.Open(catalog,position);var state=new JsonStateCodec().Read(session.ExportSave());
                Assert.That(state.Phase,Is.EqualTo(Goa2.Domain.Phase.Action),position.Id);Assert.That(state.Pending,Is.Null,position.Id);
                Assert.That(state.Execution,Is.Null,position.Id);Assert.That(state.ActiveSeat,Is.Not.Null,position.Id);
                Assert.That(metadata.Single(m=>m["id"]!.Value<string>()==position.Id)["instructions"]?.Value<string>(),Does.Contain("操作"),position.Id);
            }
        }
        [Test]
        public void OpeningTheSamePreparedPositionCreatesIndependentMatches()
        {
            var catalog=BattlefieldTests.Catalog(); var position=DebugPositions.Read(Prepared()).First();
            var first=DebugPositions.Open(catalog,position); var second=DebugPositions.Open(catalog,position);
            Assert.That(first.View(null).MatchId,Is.Not.EqualTo(second.View(null).MatchId));
            Assert.That(first.View(null).Revision,Is.EqualTo(second.View(null).Revision));
            string before=second.ExportSave(); var view=first.View(0); view.Events.Clear(); view.Units.Clear();
            Assert.That(second.ExportSave(),Is.EqualTo(before));
        }
        [TestCase("final_phase")]
        [TestCase("step_expectation")]
        public void FailedPreparedPositionDoesNotReturnAPartiallyExecutedSession(string failure)
        {
            var document=JObject.Parse(Prepared()); var first=(JObject)document["Presets"]![0]!;
            if(failure=="final_phase") first["Phase"]="Finished";
            else first["Scenario"]!["Steps"]![0]!["Expect"]!["Gold"]=new JObject { ["p1"]=999 };
            var position=DebugPositions.Read(document.ToString()).First(); GameSession? result=null;
            Assert.Throws<InvalidDataException>(()=>result=DebugPositions.Open(BattlefieldTests.Catalog(),position));
            Assert.That(result,Is.Null);
        }
        [TestCase("duplicate_id")]
        [TestCase("unknown_field")]
        [TestCase("boolean_version")]
        [TestCase("formal_session")]
        [TestCase("unchecked_replay")]
        public void PreparedDataRejectsInvalidOrUnverifiedDefinitions(string failure)
        {
            var document=JObject.Parse(Prepared()); var presets=(JArray)document["Presets"]!;
            switch(failure)
            {
                case "duplicate_id": presets[1]!["Id"]=presets[0]!["Id"]!.DeepClone(); break;
                case "unknown_field": document["Extra"]=true; break;
                case "boolean_version": document["SchemaVersion"]=true; break;
                case "formal_session": presets[0]!["Scenario"]!["Sandbox"]=false; break;
                case "unchecked_replay": presets[0]!["Scenario"]!["VerifyReplayAfterEachStep"]=false; break;
            }
            Assert.Throws<InvalidDataException>(()=>DebugPositions.Read(document.ToString()));
        }
        [TestCase("scenario_path")]
        [TestCase("boolean_step")]
        public void SourceMetadataCannotEscapeTheScenarioDirectoryOrUseBooleanStepCounts(string failure)
        {
            var metadata=JArray.Parse(File.ReadAllText(Path.Combine(Root,"tools","debug-positions.json")));
            if(failure=="scenario_path") metadata[0]!["scenario"]="../outside";
            else metadata[0]!["through_step"]=true;
            string scratch=Path.Combine(Root,"artifacts","tests","debug-position-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(scratch,"tools"));
            File.WriteAllText(Path.Combine(scratch,"tools","debug-positions.json"),metadata.ToString());
            Assert.Throws<InvalidDataException>(()=>DebugPositions.Prepare(scratch));
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class SimulationTests
    {
        public static IEnumerable<TestCaseData> Cases()
        {
            int count=Setting("GOA_SIM_GAMES",2,1,64), start=Setting("GOA_SIM_SEED",17,0,1000000);
            int steps=Setting("GOA_SIM_STEPS",120,40,900);
            for(int i=0;i<count;i++) yield return new TestCaseData(start+i,steps,i%2==1).SetName($"SeededGame_{start+i}_{steps}_{(i%2==1 ? "sandbox" : "formal")}");
        }
        private static int Setting(string name,int fallback,int min,int max)
        {
            string? text=Environment.GetEnvironmentVariable(name);
            if(string.IsNullOrEmpty(text)) return fallback;
            if(!int.TryParse(text,out int value) || value<min || value>max) throw new InvalidOperationException(name+" is out of range.");
            return value;
        }
        [TestCaseSource(nameof(Cases))]
        [Timeout(3600000)]
        public void SeededGame(int seed,int steps,bool sandbox)
        {
            var catalog=BattlefieldTests.Catalog();
            string run=Environment.GetEnvironmentVariable("GOA_SIM_RUN") ?? Guid.NewGuid().ToString("N");
            Assert.That(Guid.TryParseExact(run,"N",out _),Is.True,"GOA_SIM_RUN must be a GUID in N format.");
            string output=Path.Combine(ContentTests.Root(),"artifacts","simulations",run,$"seed-{seed}-{(sandbox ? "sandbox" : "formal")}");
            var driver=new SimulationDriver(catalog,seed,steps,sandbox,output);
            driver.Generate();
            TestContext.WriteLine("Simulation artifacts: "+output);
            Assert.That(driver.Report.Passed,Is.True,driver.Report.Failure+"; artifacts: "+output);
            Assert.That(driver.Report.Steps==steps || driver.Report.StopReason=="match_finished",Is.True);
            if(steps>=120 && driver.Report.StopReason!="match_finished") Assert.That(driver.Report.FinalRound,Is.GreaterThan(1));
            var input=ScenarioRunner.Load(File.ReadAllText(Path.Combine(output,driver.Definition.Id+".json")));
            var replay=new ScenarioRunner(catalog,input);
            while(!replay.Complete) replay.Next();
            File.WriteAllText(Path.Combine(output,"scenario-replay.json"),JsonConvert.SerializeObject(replay.Report,Formatting.Indented));
            driver.RecordScenarioReplay(replay.Report.Passed && replay.Session.ExportSave()==driver.FinalSave,string.Join("; ",replay.Report.Steps.SelectMany(s=>s.Errors)));
            Assert.That(driver.Report.ScenarioReplayPassed,Is.True,driver.Report.Failure);
            Assert.That(replay.Session.ExportSave(),Is.EqualTo(driver.FinalSave),"Exported scenario must reproduce every accepted command.");
        }
        [Test]
        public void IdenticalSeedProducesTheSameScenarioAndFinalSave()
        {
            var catalog=BattlefieldTests.Catalog();
            var first=new SimulationDriver(catalog,45,45,true,null); first.Generate();
            var second=new SimulationDriver(catalog,45,45,true,null); second.Generate();
            Assert.That(first.Report.Passed && second.Report.Passed,Is.True,first.Report.Failure+second.Report.Failure);
            Assert.That(first.FinalSave,Is.EqualTo(second.FinalSave));
            Assert.That(JsonConvert.SerializeObject(first.Definition),Is.EqualTo(JsonConvert.SerializeObject(second.Definition)));
        }
        [Test]
        public void InvariantsRejectOverlappingUnitsEvenWhenTheMapCellExists()
        {
            var catalog=BattlefieldTests.Catalog(); var state=new JsonStateCodec().Read(CombatFlowTests.Duel(catalog).ExportSave());
            state.Units[1].Position=state.Units[0].Position;
            Assert.Throws<InvalidOperationException>(()=>SimulationDriver.CheckInvariants(catalog,state));
        }
    }
}

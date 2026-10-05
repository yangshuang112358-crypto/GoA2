using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
    public sealed class MatchOpeningTests
    {
        [TestCase(Team.Blue)][TestCase(Team.Red)]
        public void DraftIsOneTwoOneAndBothTossesRestore(Team first)
        {
            var c=BattlefieldTests.Catalog();var g=LocalGameFactory.Create(c,"physical-opening",new[]{"A","B","C","D"},1,physicalOpening:true);
            string pose=first==Team.Red?"0,0,0,1":"1,0,0,0";
            Apply(g,0,CommandKind.ReportCoinToss,"draft:1|"+first+"|"+pose);
            int a=first==Team.Blue?0:1,b=1-a;var heroes=c.Heroes.Select(h=>h.Id).ToArray();
            string saved=g.ExportSave();Assert.That(g.Execute(b,Cmd(g,b,CommandKind.ChooseHero,heroes[0])).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(saved));
            Apply(g,a,CommandKind.ChooseHero,heroes[0]);Apply(g,b,CommandKind.ChooseHero,heroes[1]);Apply(g,b+2,CommandKind.ChooseHero,heroes[2]);Apply(g,a+2,CommandKind.ChooseHero,heroes[3]);
            Assert.That(g.View(0).Opening.DraftComplete,Is.True);
            foreach(int captain in new[]{0,1})while(g.View(captain).Deployments.Count>0){var d=g.View(captain).Deployments.First();Apply(g,captain,CommandKind.DeployHero,target:d.Key,cell:d.Value.First());}
            Assert.That(g.View(0).Opening.TossId,Is.EqualTo("opening:2"));Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Deployment));
            Apply(g,0,CommandKind.ReportCoinToss,"opening:2|Red|0,0,0,1");Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Planning));
            Assert.That(LocalGameFactory.Restore(c,g.ExportSave()).ExportSave(),Is.EqualTo(g.ExportSave()));
        }
        [Test] public void OnlyHostReportsAndFourDistinctVotesAreRequired()
        {
            var c=BattlefieldTests.Catalog();var g=LocalGameFactory.Create(c,"reroll",new[]{"A","B","C","D"},1,physicalOpening:true);
            var before=g.ExportSave();Assert.That(g.Execute(1,Cmd(g,1,CommandKind.ReportCoinToss,"draft:1|Red|0,0,0,1")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
            foreach(string pose in new[]{"NaN,0,0,1","0,0,0,2","1,0,0,0"})Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ReportCoinToss,"draft:1|Red|"+pose)).Accepted,Is.False);
            Apply(g,0,CommandKind.MarkCoinStuck,"draft:1");
            for(int seat=0;seat<3;seat++)Apply(g,seat,CommandKind.VoteCoinReroll,"draft:1");
            Apply(g,0,CommandKind.VoteCoinReroll,"draft:1");Assert.That(g.View(0).Opening.Status,Is.EqualTo("stuck"));
            g=LocalGameFactory.Restore(c,g.ExportSave());Apply(g,3,CommandKind.VoteCoinReroll,"draft:1");Assert.That(g.View(0).Opening.TossId,Is.EqualTo("draft:2"));Assert.That(g.View(0).Opening.RerollVotes,Is.Empty);
            before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ReportCoinToss,"draft:1|Red|0,0,0,1")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
        }
        [Test] public void LegacyMatchDoesNotAcquireAnOpeningState()
        {
            var c=BattlefieldTests.Catalog();var g=LocalGameFactory.Create(c,"legacy97",new[]{"A","B","C","D"},1,engineVersion:97);
            Assert.That(g.View(0).Opening,Is.Null);Assert.That(g.ExportSave(),Does.Not.Contain("Opening"));
            Apply(g,3,CommandKind.ChooseHero,c.Heroes[0].Id);Assert.That(LocalGameFactory.Restore(c,g.ExportSave()).ExportSave(),Is.EqualTo(g.ExportSave()));
        }
    }
}

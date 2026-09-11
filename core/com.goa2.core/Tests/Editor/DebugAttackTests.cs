using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class DebugAttackTests
    {
        [Test]
        public void DefenseSuccessAndWrongResponderKeepHeroAndPlanningState()
        {
            var catalog=BattlefieldTests.Catalog();var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugAttack,"hero:1|0");string before=game.ExportSave();
            Assert.That(game.Execute(2,Cmd(game,2,CommandKind.DeclineDefense)).Accepted,Is.False);
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DebugAttack,"hero:3|3")).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
            var defense=game.View(1).DefenseOptions.First(o=>o.Assessment.Successful);
            Apply(game,1,CommandKind.Defend,defense.CardId);
            Assert.That(game.View(0).Units.Any(u=>u.Id=="hero:1"),Is.True);
            Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Planning));
            Assert.That(game.View(1).OwnCards.Single(c=>c.CardId==defense.CardId).Zone,Is.EqualTo(CardZone.Discarded));
        }
        [TestCase("hero:1|-1")]
        [TestCase("hero:1|100")]
        [TestCase("hero:1|abc")]
        [TestCase("hero:1")]
        public void InvalidPowerIsAtomic(string value)
        {
            var game=BattlefieldTests.Ready(BattlefieldTests.Catalog());string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DebugAttack,value)).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
        }
        [Test]
        public void FormalAndOldEngineGamesCannotUseDebugAttack()
        {
            var catalog=BattlefieldTests.Catalog();var old=BattlefieldTests.Ready(catalog,8);string before=old.ExportSave();
            Assert.That(old.Execute(0,Cmd(old,0,CommandKind.DebugAttack,"hero:1|5")).Accepted,Is.False);
            Assert.That(old.ExportSave(),Is.EqualTo(before));
            var formal=LocalGameFactory.Create(catalog,"formal-debug",new[]{"A","B","C","D"},42,false);
            before=formal.ExportSave();
            Assert.That(formal.Execute(0,Cmd(formal,0,CommandKind.DebugAttack,"hero:1|5")).Code,Is.EqualTo("debug_disabled"));
            Assert.That(formal.ExportSave(),Is.EqualTo(before));
        }
        [Test]
        public void HeroAttackOffersDefenseRestoresAndAwardsDefeatWithoutConsumingTurn()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugAttack,"hero:1|5");
            Assert.That(game.View(1).Pending?.Kind, Is.EqualTo("defense"));
            Assert.That(game.View(1).Attack?.BaseAttack, Is.EqualTo(5));
            game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Phase, Is.EqualTo(Phase.Planning));
            Assert.That(game.View(0).Turn, Is.EqualTo(1));
            Assert.That(game.View(0).Players[1].AwaitingRespawn, Is.True);
            Assert.That(game.View(0).Players[0].Gold, Is.GreaterThan(0));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void MinionAttackHonorsProtectionAndRejectsFriendsAtomically()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            string before=game.ExportSave();
            var heavy=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="heavy");
            foreach(string id in new[]{heavy.Id,"hero:2","missing"})
                Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DebugAttack,id+"|5")).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            var minion=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee");
            Apply(game,0,CommandKind.DebugAttack,minion.Id+"|5");
            Assert.That(game.View(0).Units.Any(u=>u.Id==minion.Id), Is.False);
            Assert.That(game.View(0).Players[0].Gold, Is.GreaterThan(0));
        }
    }
}

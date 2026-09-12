using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class OnwardTests
    {
        internal const string Onward="brogan-05-勇往直前";
        private static GameSession Setup(ContentCatalog catalog)
        {
            var game=BraveChargeTests.Setup(catalog,false,Onward);Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(1,-8));Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(3,-7));return game;
        }
        [Test]
        public void ExactCardTextAndVersionGate()
        {
            var c=BattlefieldTests.Catalog().Card(Onward);Assert.That(c.PrimaryValue,Is.EqualTo(7));Assert.That(c.Initiative,Is.EqualTo(8));Assert.That(c.SecondaryMovement,Is.EqualTo(3));Assert.That(c.SecondaryDefense,Is.EqualTo(8));Assert.That(c.Subtype,Is.Null);
            Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,27),Is.False);c.Text+="任意距离。";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
        }
        [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void TwoThreeAndFourCellsResolveTheCorrespondingAttack(int distance)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);game=ChargeTests.Restore(catalog,game);
            Assert.That(game.View(0).EffectMoves.Select(m=>m.Path.Count-1).Distinct(),Is.EquivalentTo(new[]{2,3,4}));Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(1+distance,-8));game=ChargeTests.Restore(catalog,game);
            string target=distance==4?"hero:1":"minion:-1,-3";Apply(game,0,CommandKind.ChooseAttackTarget,target);if(distance==4){Assert.That(game.View(0).Attack!.FinalAttack,Is.EqualTo(7));Apply(game,1,CommandKind.DeclineDefense);}
            Assert.That(game.View(0).Events.Single(e=>e.Kind=="UnitMoved").Path.Count,Is.EqualTo(distance+1));Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(distance==4?1:2));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void BlockingTheFourthSpacePreservesTwoAndThreeStepChoices()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(5,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.Select(m=>m.Destination),Is.EquivalentTo(new[]{new Hex(3,-8),new Hex(4,-8)}));
        }
        [Test]
        public void MovementAndRangeBonusesDoNotAlterMandatoryDistancesOrMeleeReach()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].MovementBonus=5;state.Players[0].RangeBonus=5;state.Players[0].RangedBonus=5;game=new GameSession(catalog,codec,state);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.All(m=>m.Path.Count>=3 && m.Path.Count<=5),Is.True);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(3,-8));Assert.That(game.View(0).AttackTargets,Is.EqualTo(new[]{"minion:-1,-3"}));
        }
        [Test]
        public void InvalidAndDuplicateFourStepChoiceIsAtomic()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);string before=game.ExportSave();Assert.That(game.View(null).EffectMoves,Is.Empty);
            foreach(var cmd in new[]{Cmd(game,0,CommandKind.ChooseEffectMove,"skip"),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(2,-8)),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(6,-8)),Cmd(game,1,CommandKind.ChooseEffectMove,destination:new Hex(5,-8))})
            {Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var move=Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(5,-8));Assert.That(game.Execute(0,move).Accepted,Is.True);game=ChargeTests.Restore(catalog,game);var after=game.ExportSave();Assert.That(game.Execute(0,move).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
        }
        [Test]
        public void FourStepAttackStillAllowsMeleeBlockCounter()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-8));Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.Defend,"tigerclaw-14-近身格挡");game=ChargeTests.Restore(catalog,game);
            Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("forced_discard"));Apply(game,0,CommandKind.ForcedDiscard,"brogan-00-猛攻");Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void Engine27KeepsItsOriginalTwoOrThreeStepWindow()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine27-bravecharge-move.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Onward));Assert.That(game.View(0).EffectMoves.All(m=>m.Path.Count==3 || m.Path.Count==4),Is.True);
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(4,-8));Apply(game,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");ChargeTests.Restore(catalog,game);
        }
    }
}

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
    public sealed class BraveChargeTests
    {
        internal const string Brave="brogan-03-奋勇冲锋";
        internal static GameSession Setup(ContentCatalog catalog,bool minion=true)
        {
            var game=ChargeTests.Setup(catalog,minion:false,counter:true,card:Brave);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(2,-8));
            if(minion)Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(4,-7));return game;
        }
        [Test]
        public void ExactCardAndVersion()
        {
            var c=BattlefieldTests.Catalog().Card(Brave);Assert.That(c.PrimaryValue,Is.EqualTo(6));Assert.That(c.Initiative,Is.EqualTo(7));Assert.That(c.SecondaryDefense,Is.EqualTo(8));Assert.That(c.SecondaryMovement,Is.EqualTo(3));
            Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,26),Is.False);c.Text+="最多。";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
        }
        [TestCase(2)] [TestCase(3)]
        public void EachSpecifiedDistanceLeadsToItsOwnAdjacentAttack(int distance)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);game=ChargeTests.Restore(catalog,game);
            Assert.That(game.View(0).EffectMoves.Select(m=>m.Path.Count-1).Distinct(),Is.EquivalentTo(new[]{2,3}));
            var end=new Hex(2+distance,-8);Apply(game,0,CommandKind.ChooseEffectMove,cell:end);game=ChargeTests.Restore(catalog,game);
            string target=distance==2?"minion:-1,-3":"hero:1";Apply(game,0,CommandKind.ChooseAttackTarget,target);if(distance==3)Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(end));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(distance==2?2:1));Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void NoTargetAtTwoStepsDoesNotPreventTheValidThreeStepRoute()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.Select(m=>m.Destination),Is.EqualTo(new[]{new Hex(5,-8)}));
        }
        [TestCase(4)] [TestCase(5)]
        public void ABlockAtTheSecondOrThirdCellStopsOnlyRoutesThatCrossIt(int x)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(x,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.Any(m=>m.Destination==new Hex(5,-8)),Is.False);Assert.That(game.View(0).EffectMoves.Any(m=>m.Destination==new Hex(4,-8)),Is.EqualTo(x==5));
        }
        [Test]
        public void MovementBonusDoesNotCreateOneOrFourStepChoices()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].MovementBonus=5;game=new GameSession(catalog,codec,state);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.All(m=>m.Path.Count==3 || m.Path.Count==4),Is.True);Assert.That(game.View(0).EffectMoves,Is.Not.Empty);
        }
        [Test]
        public void InvalidMoveAndDuplicateThreeStepCommandDoNotPartiallyMove()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);var before=game.ExportSave();Assert.That(game.View(1).EffectMoves,Is.Empty);
            foreach(var cmd in new[]{Cmd(game,0,CommandKind.ChooseEffectMove,"skip"),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(3,-8)),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(6,-8)),Cmd(game,1,CommandKind.ChooseEffectMove,destination:new Hex(5,-8))})
            {Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var move=Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(5,-8));Assert.That(game.Execute(0,move).Accepted,Is.True);game=ChargeTests.Restore(catalog,game);var after=game.ExportSave();Assert.That(game.Execute(0,move).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
        }
        [Test]
        public void ThreeStepMeleeAttackCanTriggerTheDefendersCounter()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-8));Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Apply(game,1,CommandKind.Defend,"tigerclaw-14-近身格挡");game=ChargeTests.Restore(catalog,game);Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("forced_discard"));Apply(game,0,CommandKind.ForcedDiscard,"brogan-00-猛攻");Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(5,-8)));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void RealEngine26MoveWindowStillUsesOnlyTwoSteps()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine26-charge-move.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Brave));
            Assert.That(game.View(0).EffectMoves.All(m=>m.Path.Count==3),Is.True);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-8));Apply(game,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");ChargeTests.Restore(catalog,game);
        }
    }
}

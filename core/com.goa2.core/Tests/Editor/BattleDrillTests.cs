using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
using static Goa2.Tests.DrillTests;

namespace Goa2.Tests
{
    public sealed class BattleDrillTests
    {
        internal const string BattleDrill="sabina-14-战斗演练";
        [Test]
        public void ExactContractAndPreviousEngineRemainSeparate()
        {
            var c=BattlefieldTests.Catalog().Card(BattleDrill);Assert.That(c.PrimaryFamily,Is.EqualTo("movement"));Assert.That(c.PrimaryValue,Is.EqualTo(3));Assert.That(c.SecondaryMovement,Is.Null);Assert.That(c.SecondaryDefense,Is.EqualTo(4));Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(c.Initiative,Is.EqualTo(10));
            Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,30),Is.False);c.Text+="基础攻击。";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void MovementAndStayCreateOneRoundAuraAfterTheirChoice(bool stay)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:BattleDrill);Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).Effects,Is.Empty);Assert.That(game.View(0).Pending!.ResumeAt,Is.EqualTo("primary_movement"));ChargeTests.Restore(catalog,game);Apply(game,0,CommandKind.ChooseEffectMove,stay?"skip":"",cell:new Hex(4,-8));var e=game.View(0).Effects.Single();Assert.That(e.SourceCardId,Is.EqualTo(BattleDrill));Assert.That(e.Window.EndTurn,Is.EqualTo(4));ChargeTests.Restore(catalog,game);
        }
        [TestCase("brogan-02-投掷飞斧")] [TestCase("brogan-00-猛攻")]
        public void AlliedOrdinaryAndBasicAttacksReceiveTwoDifferentMinionSources(string attack)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:BattleDrill);Activate(game);NextAttack(game,attack);Apply(game,2,CommandKind.BeginPrimary);if(attack.Contains("投掷"))Apply(game,2,CommandKind.ChooseOptionalDiscard,"skip");Apply(game,2,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(2).Attack!.EnemySupportSources,Is.EquivalentTo(new[]{Melee,Heavy}));Assert.That(game.View(2).Attack!.FinalAttack,Is.EqualTo(catalog.Card(attack).PrimaryValue+2));Assert.That(game.View(0).Units.Single(u=>u.Id==Melee).Kind,Is.EqualTo("melee"));Assert.That(game.View(0).Units.Single(u=>u.Id==Heavy).Kind,Is.EqualTo("heavy"));ChargeTests.Restore(catalog,game);Apply(game,1,CommandKind.DeclineDefense);Assert.That(game.View(0).Effects.Single().SourceCardId,Is.EqualTo(BattleDrill));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void OwnOrdinaryAttackGainsConversionButNotDoubleCountingForAdjacentMinion()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:BattleDrill);Activate(game);Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(4,-7));Apply(game,0,CommandKind.DebugTeleport,Heavy,cell:new Hex(7,-9));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(5,-7));NextAttack(game,sabina:"sabina-01-拔枪",actor:0);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Assert.That(game.View(0).Attack!.EnemySupportSources,Is.EqualTo(new[]{Melee}));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void EnemyAttackKeepsTheMeleeGuardInsteadOfConvertingIt()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:BattleDrill);Activate(game);Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(5,-7));Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(5,-8));NextAttack(game,wasp:"wasp-00-闪耀之刃",actor:3);Apply(game,3,CommandKind.BeginPrimary);Apply(game,3,CommandKind.ChooseAttackTarget,"hero:2");Assert.That(game.View(3).Attack!.FriendlyGuardSources,Does.Contain(Melee));ChargeTests.Restore(catalog,game);
        }
        [TestCase("retrieve")] [TestCase("defeat")] [TestCase("round")]
        public void SourceStateAndRoundBoundaryCancelTheEffect(string reason)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:BattleDrill);Activate(game);
            if(reason=="retrieve")
            {Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(4,-7));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(5,-8));NextAttack(game,"brogan-10-吟游诗人");Apply(game,2,CommandKind.BeginPrimary);Apply(game,2,CommandKind.ChooseEffectTarget,"hero:0");Apply(game,0,CommandKind.ChooseRecoveredCard,BattleDrill);}
            else if(reason=="defeat")Apply(game,0,CommandKind.DebugDefeatHero,"hero:0",target:1);
            else Apply(game,0,CommandKind.DebugAdvance,"round");
            Assert.That(game.View(0).Effects,Is.Empty);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void FastTravelDoesNotRunTheCardText()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:BattleDrill);Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:game.View(0).DebugTeleports["hero:0"].First(h=>catalog.Cell(h)!.Region=="blueFountain"));Apply(game,0,CommandKind.Move,cell:game.View(0).FastMoves.First().Destination,mode:MoveMode.Fast);Assert.That(game.View(0).Effects,Is.Empty);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void WrongChooserCannotPartiallyActivateAnAuraAndRepeatedChoiceIsIdempotent()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:BattleDrill);Apply(game,0,CommandKind.BeginPrimary);string before=game.ExportSave();Assert.That(game.Execute(1,Cmd(game,1,CommandKind.ChooseEffectMove,"skip")).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));var c=Cmd(game,0,CommandKind.ChooseEffectMove,"skip");Assert.That(game.Execute(0,c).Accepted,Is.True);before=game.ExportSave();Assert.That(game.Execute(0,c).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(before));
        }
        [Test]
        public void PreviousDrillAttackFixtureDoesNotGainTheUpgrade()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine30-drill-attack.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(BattleDrill));Assert.That(game.View(2).Attack!.EnemySupport,Is.EqualTo(2));Apply(game,1,CommandKind.DeclineDefense);ChargeTests.Restore(catalog,game);
        }
    }
}

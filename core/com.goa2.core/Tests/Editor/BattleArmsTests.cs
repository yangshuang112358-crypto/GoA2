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
    public sealed class BattleArmsTests
    {
        internal const string Arms="sabina-16-战斗武装";
        [Test]
        public void ExactCardAndLegacyGate()
        {
            var c=BattlefieldTests.Catalog().Card(Arms);Assert.That(c.PrimaryValue,Is.EqualTo(3));Assert.That(c.SecondaryMovement,Is.Null);Assert.That(c.SecondaryDefense,Is.EqualTo(4));Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(c.Initiative,Is.EqualTo(10));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,31),Is.False);c.Text+="仅基础攻击。";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void MoveOrStayStartsOnePublicRoundEffect(bool stay)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:Arms);Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(null).Effects,Is.Empty);Assert.That(game.View(0).Pending!.ResumeAt,Is.EqualTo("primary_movement"));ChargeTests.Restore(catalog,game);Apply(game,0,CommandKind.ChooseEffectMove,stay?"skip":"",cell:new Hex(4,-8));Assert.That(game.View(null).Effects.Single().SourceCardId,Is.EqualTo(Arms));Assert.That(game.View(null).Effects.Single().Window.EndTurn,Is.EqualTo(4));ChargeTests.Restore(catalog,game);
        }
        [TestCase("melee",1,2)] [TestCase("ranged",1,2)] [TestCase("heavy",1,2)]
        [TestCase("melee",2,1)] [TestCase("ranged",2,1)] [TestCase("heavy",2,1)]
        [TestCase("melee",3,0)] [TestCase("ranged",3,0)] [TestCase("heavy",3,0)]
        public void EachRealMinionKindSupportsTwiceAdjacentOnceAtTwoAndNeverAtThree(string kind,int distance,int support)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:Arms);Activate(game);NextAttack(game);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());
            // Isolate taxonomy/radius math without changing the authoritative scenario's actual minions.
            state.Units.RemoveAll(u=>u.Kind!="hero");state.Units.Single(u=>u.Seat==0).Position=new Hex(5,-6);state.Units.Add(new UnitState{Id="subject",Team=Team.Blue,Kind=kind,Position=new Hex(7-distance,-7)});
            var card=catalog.Card("brogan-00-猛攻");var attack=CombatMath.Attack(state,card,2,"hero:1",minionKinds:EffectRules.MinionCombatKinds(catalog,state,card,2));
            Assert.That(attack.EnemySupport,Is.EqualTo(support));Assert.That(attack.EnemySupportSources,Is.EqualTo(Enumerable.Repeat("subject",support)));Assert.That(state.Units.Single(u=>u.Id=="subject").Kind,Is.EqualTo(kind));
        }
        [TestCase("brogan-00-猛攻")] [TestCase("brogan-02-投掷飞斧")]
        public void RealAlliedAttackUsesTwoAdjacentAndOneDistantContribution(string card)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:Arms);Activate(game);Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(6,-7));Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(5,-7));NextAttack(game,card);Apply(game,2,CommandKind.BeginPrimary);if(card.Contains("投掷"))Apply(game,2,CommandKind.ChooseOptionalDiscard,"skip");Apply(game,2,CommandKind.ChooseAttackTarget,"hero:1");
            var a=game.View(2).Attack!;Assert.That(a.EnemySupportSources,Is.EquivalentTo(new[]{Melee,Melee,Heavy}));Assert.That(a.FinalAttack,Is.EqualTo(catalog.Card(card).PrimaryValue+3));Assert.That(CombatMath.Defense(a,0,0,ignoreMinions:true).AttackCompared,Is.EqualTo(catalog.Card(card).PrimaryValue));ChargeTests.Restore(catalog,game);Apply(game,1,CommandKind.DeclineDefense);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void SourceOwnOrdinaryAttackAlsoReceivesDualSupport()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:Arms);Activate(game);Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(4,-7));Apply(game,0,CommandKind.DebugTeleport,Heavy,cell:new Hex(7,-9));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(5,-7));NextAttack(game,sabina:"sabina-01-拔枪",actor:0);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Assert.That(game.View(0).Attack!.EnemySupportSources,Is.EqualTo(new[]{Melee,Melee}));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void OutsideTheAuraUsesRealKindsAndRangedBonusDoesNotEnlargeAura()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:Arms);Activate(game);NextAttack(game);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].RangedBonus=9;state.Units.Single(u=>u.Seat==0).Position=new Hex(1,-8);var card=catalog.Card("brogan-00-猛攻");Assert.That(EffectRules.MinionCombatKinds(catalog,state,card,2),Is.Empty);
        }
        [Test]
        public void EnemyAttackDoesNotGiveTheDefendingTeamDualGuard()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:Arms);Activate(game);Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(5,-7));Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(5,-8));NextAttack(game,wasp:"wasp-00-闪耀之刃",actor:3);Apply(game,3,CommandKind.BeginPrimary);Apply(game,3,CommandKind.ChooseAttackTarget,"hero:2");Assert.That(game.View(3).Attack!.FriendlyGuardSources,Is.EqualTo(new[]{Melee}));ChargeTests.Restore(catalog,game);
        }
        [TestCase("defeat")] [TestCase("round")]
        public void AuraCannotOutliveItsSourceOrRound(string reason)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:Arms);Activate(game);if(reason=="defeat")Apply(game,0,CommandKind.DebugDefeatHero,"hero:0",target:1);else Apply(game,0,CommandKind.DebugAdvance,"round");Assert.That(game.View(0).Effects,Is.Empty);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void ChallengerIgnoresBothContributionsInARealDefenseWindow()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,interference:"silence",card:Arms);Apply(game,3,CommandKind.BeginPrimary);Activate(game);Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(7,-9));NextAttack(game,wasp:"arien-07-潮水");Apply(game,2,CommandKind.BeginPrimary);Apply(game,2,CommandKind.ChooseAttackTarget,"hero:3");
            var attack=game.View(3).Attack!;Assert.That(attack.EnemySupportSources,Is.EquivalentTo(new[]{Melee,Heavy,Heavy}));var defense=game.View(3).DefenseOptions.Single(o=>o.CardId=="arien-13-挑战者").Assessment;Assert.That(defense.IgnoresMinions,Is.True);Assert.That(defense.AttackCompared,Is.EqualTo(attack.BaseAttack+attack.AttackBonus));ChargeTests.Restore(catalog,game);Apply(game,3,CommandKind.Defend,"arien-13-挑战者");Assert.That(game.View(3).Players[3].AwaitingRespawn,Is.False);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void FastMovementCreatesNoAura()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,card:Arms);Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:game.View(0).DebugTeleports["hero:0"].First(h=>catalog.Cell(h)!.Region=="blueFountain"));Apply(game,0,CommandKind.Move,cell:game.View(0).FastMoves.First().Destination,mode:MoveMode.Fast);Assert.That(game.View(0).Effects,Is.Empty);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void FrozenSingleKindDefenseRemainsByteExact()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine31-battledrill-defense.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Arms));Assert.That(game.View(2).Attack!.EnemySupport,Is.EqualTo(2));Apply(game,1,CommandKind.DeclineDefense);ChargeTests.Restore(catalog,game);
        }
    }
}

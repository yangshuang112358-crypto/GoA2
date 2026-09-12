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
    public sealed class BoomerangTests
    {
        internal const string Boomerang="wasp-02-回旋镖",Dodge="tigerclaw-18-躲闪";
        internal static GameSession Setup(ContentCatalog catalog)
        {
            var game=LocalGameFactory.Create(catalog,"boomerang",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"wasp,tigerclaw,brogan,arien");Apply(game,0,CommandKind.DebugEquipCard,Boomerang,target:0);Apply(game,0,CommandKind.DebugEquipCard,Dodge,target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(5,-8)); // Tigerclaw already starts at (7,-9).
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-9));Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(7,-8));
            string[] cards={Boomerang,"tigerclaw-07-伺机待发","brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(0));return game;
        }
        private static GameSession Restore(ContentCatalog catalog,GameSession game)
        {string save=game.ExportSave();var result=LocalGameFactory.Restore(catalog,save);Assert.That(result.ExportSave(),Is.EqualTo(save));return result;}
        [Test]
        public void ExactCardAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Boomerang);
            Assert.That(card.PrimaryValue,Is.EqualTo(3));Assert.That(card.Initiative,Is.EqualTo(9));Assert.That(card.Subtype,Is.EqualTo("远程"));Assert.That(card.SubtypeValue,Is.EqualTo(3));
            Assert.That(card.SecondaryMovement,Is.EqualTo(4));Assert.That(card.SecondaryDefense,Is.EqualTo(3));
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,17),Is.False);
        }
        [TestCase(1,0)] [TestCase(0,1)] [TestCase(-1,1)] [TestCase(-1,0)] [TestCase(0,-1)] [TestCase(1,-1)]
        public void AllSixStraightDirectionsAreExcludedAtEveryDistanceIncludingAdjacent(int dx,int dy)
        {
            var catalog=BattlefieldTests.Catalog();var state=new JsonStateCodec().Read(Setup(catalog).ExportSave());
            var source=state.Units.Single(u=>u.Seat==0);var target=state.Units.Single(u=>u.Seat==1);
            // Geometry-only query positions need not fit this map edge; live scenarios use actual board cells.
            source.Position=new Hex(0,0);
            for(int distance=1;distance<=3;distance++)
            {
                target.Position=new Hex(dx*distance,dy*distance);
                Assert.That(CombatRules.AttackTargets(catalog,state,0),Does.Not.Contain(target.Id));
            }
        }
        [TestCase(1,1,0,0,true)] [TestCase(2,1,0,0,true)] [TestCase(3,1,0,0,false)] [TestCase(3,1,1,0,true)] [TestCase(3,1,0,3,false)]
        public void NonStraightTargetsRespectTheRangeAndCorrectItemType(int dx,int dy,int ranged,int radius,bool legal)
        {
            var catalog=BattlefieldTests.Catalog();var state=new JsonStateCodec().Read(Setup(catalog).ExportSave());
            state.Units.Single(u=>u.Seat==0).Position=new Hex(0,0);state.Units.Single(u=>u.Seat==1).Position=new Hex(dx,dy);
            state.Players[0].RangedBonus=ranged;state.Players[0].RangeBonus=radius;
            Assert.That(CombatRules.AttackTargets(catalog,state,0).Contains("hero:1"),Is.EqualTo(legal));
        }
        [TestCase(true)] [TestCase(false)]
        public void LegalRangedAttackCanBeBlockedOrNumericallyDefendedAndRestores(bool block)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);game=Restore(catalog,game);
            Assert.That(game.View(0).AttackTargets,Is.EqualTo(new[]{"hero:1"}));Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");game=Restore(catalog,game);
            Assert.That(game.View(1).Attack!.Ranged,Is.True);Assert.That(game.View(1).Attack!.Unblockable,Is.False);Assert.That(game.View(1).Attack!.FinalAttack,Is.EqualTo(3));
            string defense=block?Dodge:"tigerclaw-02-偷袭";
            var option=game.View(1).DefenseOptions.Single(o=>o.CardId==defense);Assert.That(option.Block,Is.EqualTo(block));Assert.That(option.Assessment.Successful,Is.True);
            var command=Cmd(game,1,CommandKind.Defend,defense);Assert.That(game.Execute(1,command).Accepted,Is.True);game=Restore(catalog,game);string after=game.ExportSave();
            Assert.That(game.Execute(1,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.View(0).RedCrystal,Is.EqualTo(7));Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
        }
        [Test]
        public void StraightFriendlyProtectedAndWrongSeatTargetsAreRejectedWhileMinionDefeatRemainsNormal()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            string heavy=game.View(0).Units.First(u=>u.Kind=="heavy" && u.Team==Team.Red).Id;Apply(game,0,CommandKind.DebugTeleport,heavy,cell:new Hex(6,-7));
            string minion=game.View(0).Units.First(u=>u.Kind=="melee" && u.Team==Team.Red).Id;Apply(game,0,CommandKind.DebugTeleport,minion,cell:new Hex(4,-6));
            Apply(game,0,CommandKind.BeginPrimary);string before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:3"),Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:2"),Cmd(game,0,CommandKind.ChooseAttackTarget,heavy),Cmd(game,1,CommandKind.ChooseAttackTarget,"hero:1")})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            Apply(game,0,CommandKind.ChooseAttackTarget,minion);Assert.That(game.View(0).Units.Any(u=>u.Id==minion),Is.False);Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));Restore(catalog,game);
        }
        [Test]
        public void OnlyStraightEnemiesMeansNoAttackAndRangedImmunityStillExcludesAnOffLineHero()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var state=new JsonStateCodec().Read(game.ExportSave());
            state.Effects.Add(new ActiveEffect {Id="test-immunity",Kind=EffectKind.NonAdjacentRangedImmunity,ProtectedUnitId="hero:1",Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            Assert.That(CombatRules.AttackTargets(catalog,state,0),Is.Empty);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(6,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Events.Last(e=>e.Kind=="CardEffectStopped").Detail,Is.EqualTo("no_targets"));Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);Restore(catalog,game);
        }
        [Test]
        public void FrozenPincerStillCannotBeBlockedAndAcceptsNumericDefense()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine17-pincer-defense.json"));
            var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Boomerang));
            Assert.That(game.View(1).Attack!.Unblockable,Is.True);Assert.That(game.Execute(1,Cmd(game,1,CommandKind.Defend,PincerAttackTests.Lead)).Accepted,Is.False);
            Apply(game,1,CommandKind.Defend,PincerAttackTests.Numeric);Assert.That(game.View(1).RedCrystal,Is.EqualTo(7));Restore(catalog,game);
        }
    }
}

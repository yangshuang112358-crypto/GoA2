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
    public sealed class MagicWaterTests
    {
        internal const string Card="arien-09-魔法水流";
        [Test] public void ExactBindingAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Card);
            Assert.That(card.SubtypeValue,Is.EqualTo(3));Assert.That(card.Initiative,Is.EqualTo(4));
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,68),Is.False);
            card.Text+="可选";Assert.That(CombatRules.HasPrimaryProgram(card),Is.False);
        }
        [Test] public void ThirdRingIsAvailableAndPlacementCrossesMovementBoundary()
        {
            var cat=BattlefieldTests.Catalog();var game=TideTests.Ready(cat,Card,true);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Placements,Does.Contain(new Hex(6,-5)));game=ChargeTests.Restore(cat,game);
            var cmd=Cmd(game,0,CommandKind.ChoosePlacement,destination:new Hex(6,-5));Assert.That(game.Execute(0,cmd).Accepted,Is.True);
            string before=game.ExportSave();Assert.That(game.Execute(0,cmd).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,game);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(6,-5)));
            Assert.That(game.View(0).Events.Single(e=>e.Kind=="UnitPlaced").Path,Is.Empty);
            var tide=TideTests.Ready(cat);Apply(tide,0,CommandKind.BeginPrimary);Assert.That(tide.View(0).Placements,Has.No.Member(new Hex(6,-5)));
        }
        [TestCase(7,-9,7,-10,1)] [TestCase(5,-9,5,-8,2)]
        public void VacatedSpawnRuleIncludesHeroAndMinionSpawns(int x,int y,int bx,int by,int occupyingSeat)
        {
            var cat=BattlefieldTests.Catalog();var game=TideTests.Ready(cat,Card);
            Apply(game,0,CommandKind.DebugTeleport,"hero:"+occupyingSeat,cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(x,y));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Placements,Has.No.Member(new Hex(bx,by)));string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChoosePlacement,destination:new Hex(bx,by))).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,game);
        }
        [Test] public void WrongActorOccupiedSpawnAndOutsideRangeAreAtomicAndPrivate()
        {
            var cat=BattlefieldTests.Catalog();var game=TideTests.Ready(cat,Card);Apply(game,0,CommandKind.BeginPrimary);string before=game.ExportSave();
            foreach(var cmd in new[]{Cmd(game,1,CommandKind.ChoosePlacement,destination:new Hex(6,-5)),Cmd(game,0,CommandKind.ChoosePlacement,destination:new Hex(7,-9)),Cmd(game,0,CommandKind.ChoosePlacement,destination:new Hex(6,-4))})
            {Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            Assert.That(game.View(1).Placements,Is.Empty);Assert.That(game.View(null).Placements,Is.Empty);
        }
        [Test] public void AttackAndMovementBonusesCannotExtendPlacementButRangedBonusCan()
        {
            var cat=BattlefieldTests.Catalog();var game=TideTests.Ready(cat,Card);Apply(game,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(game.ExportSave());
            var initial=GameRules.LegalPlacements(cat,state,0);state.Players[0].AttackBonus=8;state.Players[0].MovementBonus=8;state.Players[0].RangeBonus=8;
            Assert.That(GameRules.LegalPlacements(cat,state,0),Is.EquivalentTo(initial));state.Players[0].RangedBonus=1;
            Assert.That(GameRules.LegalPlacements(cat,state,0).Any(p=>p.Distance(new Hex(6,-8))==4),Is.True);
        }
        [Test] public void Frozen68PlacementRetainsOriginalCapabilitiesAndBytes()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine68-tide-placement.json"));
            var game=LocalGameFactory.Restore(cat,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
            Assert.That(game.View(0).Placements,Has.No.Member(new Hex(6,-5)));Apply(game,0,CommandKind.ChoosePlacement,cell:new Hex(6,-6));ChargeTests.Restore(cat,game);
        }
    }
}

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
    public sealed class TideTests
    {
        internal const string Card="arien-07-潮水";
        internal static GameSession Ready(ContentCatalog cat,string card=Card,bool boundary=false)
        {
            var game=LocalGameFactory.Create(cat,"tide-spawns",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"arien,wasp,brogan,sabina");Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            var places=new[]{new Hex(6,-8),new Hex(7,-9),new Hex(5,-9),new Hex(7,-7)};
            for(int s=0;s<4;s++)if(game.View(0).Units.Single(u=>u.Seat==s).Position!=places[s])Apply(game,0,CommandKind.DebugTeleport,"hero:"+s,cell:places[s]);
            string[] cards={card,"wasp-06-静电封锁","brogan-06-铜墙铁壁","sabina-07-指挥"};
            for(int s=0;s<4;s++)Apply(game,s,CommandKind.SelectCard,cards[s]);
            if(boundary)Apply(game,1,CommandKind.BeginPrimary);
            OpportuneMomentTests.AdvanceTo(game,0);return game;
        }
        [Test] public void ExactTextAndVersionGate()
        {var card=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,67),Is.False);card.Text+="移动";Assert.That(CombatRules.HasPrimaryProgram(card),Is.False);}
        [Test] public void OccupiedHeroAndMinionSpawnsAllowAdjacentLandingsButEmptySpawnsDoNot()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(GameRules.LegalPlacements(cat,state,0),Does.Contain(new Hex(7,-10)),"occupied hero spawn");
            Assert.That(GameRules.LegalPlacements(cat,state,0),Does.Contain(new Hex(5,-8)),"occupied minion spawn");
            state.Units.Single(u=>u.Seat==1).Position=new Hex(8,-9);state.Units.Single(u=>u.Seat==2).Position=new Hex(8,-10);
            Assert.That(GameRules.LegalPlacements(cat,state,0),Has.No.Member(new Hex(7,-10)));
            Assert.That(GameRules.LegalPlacements(cat,state,0),Has.No.Member(new Hex(5,-8)));
        }
        [TestCase(7,-9,7,-10,1)] [TestCase(5,-9,5,-8,2)]
        public void VacatedOwnHeroOrMinionSpawnIsEmptyAfterPlacement(int x,int y,int bx,int by,int occupyingSeat)
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);
            Apply(game,0,CommandKind.DebugTeleport,"hero:"+occupyingSeat,cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(x,y));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Placements,Has.No.Member(new Hex(bx,by)));string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChoosePlacement,destination:new Hex(bx,by))).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,game);
        }
        [Test] public void PlacementCrossesStaticBoundaryAndPreservesReplayAndRetry()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat,boundary:true);Apply(game,0,CommandKind.BeginPrimary);game=ChargeTests.Restore(cat,game);
            var cmd=Cmd(game,0,CommandKind.ChoosePlacement,destination:new Hex(6,-6));Assert.That(game.Execute(0,cmd).Accepted,Is.True);
            string save=game.ExportSave();Assert.That(game.Execute(0,cmd).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(save));ChargeTests.Restore(cat,game);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(6,-6)));Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);
            Assert.That(game.View(0).Events.Single(e=>e.Kind=="UnitPlaced").Path,Is.Empty);
        }
        [Test] public void AllSpawnTypesAreRejectedAsLandingsAndChoiceIsPrivateAndAtomic()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.BeginPrimary);string before=game.ExportSave();
            foreach(var cell in cat.Cells.Where(c=>c.Spawn.EndsWith("Spawn") || c.Obstacle).Select(c=>c.Position).Concat(new[]{new Hex(6,-8),new Hex(99,99)}))
            {Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChoosePlacement,destination:cell)).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var valid=game.View(0).Placements.First();foreach(var cmd in new[]{Cmd(game,1,CommandKind.ChoosePlacement,destination:valid),Cmd(game,0,CommandKind.ChoosePlacement,"skip",destination:valid),Cmd(game,0,CommandKind.ChoosePlacement,destination:valid,mode:MoveMode.Fast)})
            {Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            Assert.That(game.View(1).Placements,Is.Empty);Assert.That(game.View(null).Placements,Is.Empty);
        }
        [Test] public void OnlyRangedBonusExtendsDistance()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(game.ExportSave());
            var initial=GameRules.LegalPlacements(cat,state,0);state.Players[0].RangeBonus=9;state.Players[0].MovementBonus=9;
            Assert.That(GameRules.LegalPlacements(cat,state,0),Is.EquivalentTo(initial));state.Players[0].RangedBonus=1;
            Assert.That(GameRules.LegalPlacements(cat,state,0).Any(p=>p.Distance(new Hex(6,-8))==3),Is.True);
            Assert.That(GameRules.LegalPlacements(cat,state,0).All(p=>p.Distance(new Hex(6,-8))<=3),Is.True);
        }
        [Test] public void NoDestinationStopsWithoutPlacing()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(0,-2));Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(0,-3));Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(0,-4));
            Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).Pending?.Kind,Is.Not.EqualTo("placement"));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitPlaced"),Is.False);ChargeTests.Restore(cat,game);
        }
        [Test] public void TidalPowerKeepsItsDifferentAdjacentEmptySpawnRule()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat,"arien-11-潮汐之力");Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).Placements,Does.Contain(new Hex(7,-10)));ChargeTests.Restore(cat,game);
        }
        [Test] public void SkillSuppressionStillRejectsTheWholePlacementActionAtomically()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);var state=new JsonStateCodec().Read(game.ExportSave());
            state.Effects.Add(new ActiveEffect{Id="suppression",SourceCardId="arien-06-打断施法",SourceUnitId="hero:1",ControllerSeat=1,Kind=EffectKind.SkillSuppression,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(state.Round,state.Turn,4,EffectDuration.ThisTurn)});
            string before=new JsonStateCodec().Write(state);
            var ex=Assert.Throws<RuleViolation>(()=>new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary}));
            Assert.That(ex.Code,Is.EqualTo("primary_restricted"));Assert.That(new JsonStateCodec().Write(state),Is.EqualTo(before));
        }
        [Test] public void Frozen67RepeatWindowKeepsExactBytes()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine67-siren-repeat.json"));
            var game=LocalGameFactory.Restore(cat,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
        }
    }
}

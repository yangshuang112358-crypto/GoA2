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
    public sealed class BlinkStrikeTests
    {
        internal const string Blink="tigerclaw-00-瞬闪打击";
        internal static GameSession Setup(ContentCatalog catalog,bool minion=true)
        {
            var game=LocalGameFactory.Create(catalog,"blink",new[]{"A","B","C","D"},42,true);Apply(game,0,CommandKind.DebugPrepare,"tigerclaw,sabina,brogan,wasp");Apply(game,0,CommandKind.DebugEquipCard,"sabina-08-带头冲锋",target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));
            if(minion)Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(5,-8));
            string[] cards={Blink,"sabina-00-近身射击","brogan-06-铜墙铁壁","wasp-07-抵挡屏障"};for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);return game;
        }
        private static void Begin(GameSession game){Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("effect_move"));Assert.That(game.View(0).Pending!.Optional,Is.False);}
        [Test]
        public void ExactBasicCardContractAndVersionGate()
        {
            var c=BattlefieldTests.Catalog().Card(Blink);Assert.That(c.PrimaryValue,Is.EqualTo(2));Assert.That(c.Initiative,Is.EqualTo(13));Assert.That(c.PrimaryCategory,Is.EqualTo("基础攻击"));Assert.That(c.SecondaryDefense,Is.EqualTo(1));Assert.That(c.SecondaryMovement,Is.EqualTo(2));
            Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,28),Is.False);c.Text+="或不移动。";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void PassesExactlyOneEnemyAndAutomaticallyAttacksThatUnit(bool minion)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Begin(game);game=ChargeTests.Restore(catalog,game);var end=new Hex(minion?4:8,-8);
            Assert.That(game.View(0).EffectMoves.Single(m=>m.Destination==end).Path,Is.EqualTo(new[]{new Hex(6,-8),new Hex(minion?5:7,-8),end}));
            Apply(game,0,CommandKind.ChooseEffectMove,cell:end);game=ChargeTests.Restore(catalog,game);
            if(!minion){Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("defense"));Assert.That(game.View(0).Attack!.TargetUnitId,Is.EqualTo("hero:1"));Assert.That(game.View(0).Attack!.FinalAttack,Is.EqualTo(2));Apply(game,1,CommandKind.DeclineDefense);}
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(end));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(minion?2:1));Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackTargetChoiceRequired"),Is.False);Assert.That(game.View(0).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(1));Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));ChargeTests.Restore(catalog,game);
        }
        [TestCase(false)] [TestCase(true)]
        public void EmptyOrFriendlyMidpointCannotBeCrossed(bool friendly)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-7));if(friendly)Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(7,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves,Is.Empty);Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved" || e.Kind=="AttackDeclared"),Is.False);Assert.That(game.View(0).Pending,Is.Null);ChargeTests.Restore(catalog,game);
        }
        [TestCase(false)] [TestCase(true)]
        public void OccupiedOrOffMapLandingStopsTheWholeRoute(bool edge)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);
            if(edge){Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));}
            else Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves,Is.Empty);Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved" || e.Kind=="AttackDeclared"),Is.False);
        }
        [Test]
        public void IllegalOrDuplicateMovementCannotChangeTheLockedTarget()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Begin(game);string before=game.ExportSave();Assert.That(game.View(1).EffectMoves,Is.Empty);Assert.That(game.View(null).EffectMoves,Is.Empty);
            foreach(var cmd in new[]{Cmd(game,0,CommandKind.ChooseEffectMove,"skip"),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(7,-8)),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(8,-8),mode:MoveMode.Fast),Cmd(game,1,CommandKind.ChooseEffectMove,destination:new Hex(8,-8)),Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:3")})
            {Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var move=Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(8,-8));Assert.That(game.Execute(0,move).Accepted,Is.True);game=ChargeTests.Restore(catalog,game);string after=game.ExportSave();Assert.That(game.Execute(0,move).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:3")).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(after));Assert.That(game.View(0).Attack!.TargetUnitId,Is.EqualTo("hero:1"));
        }
        [TestCase(false)] [TestCase(true)]
        public void NumericOrConditionalPrimaryDefenseKeepsTigerOnTheFarSide(bool block)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);if(block)Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(7,-9));Begin(game);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-8));game=ChargeTests.Restore(catalog,game);
            Assert.That(game.View(1).DefenseOptions.Any(o=>o.CardId=="sabina-08-带头冲锋"),Is.EqualTo(block));Apply(game,1,CommandKind.Defend,block?"sabina-08-带头冲锋":"sabina-01-拔枪");Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(8,-8)));Assert.That(game.View(0).Units.Any(u=>u.Seat==1),Is.True);Assert.That(game.View(0).Players[0].Gold,Is.Zero);ChargeTests.Restore(catalog,game);
        }
        [TestCase(false)] [TestCase(true)]
        public void HeavyProtectionIsCheckedBeforeCrossingAndUnprotectedKillAdvances(bool unprotected)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-7));if(unprotected)foreach(var u in game.View(0).Units.Where(u=>u.Team==Team.Red && u.Kind!="hero" && u.Kind!="heavy").ToList())Apply(game,0,CommandKind.DebugRemoveMinion,u.Id);
            string heavy=game.View(0).Units.Single(u=>u.Team==Team.Red && u.Kind=="heavy").Id;Apply(game,0,CommandKind.DebugTeleport,heavy,cell:new Hex(7,-8));Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).EffectMoves.Any(m=>m.Destination==new Hex(8,-8)),Is.EqualTo(unprotected));
            if(unprotected){Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-8));Assert.That(game.View(0).BlueMarks,Is.EqualTo(1));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(4));Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));ChargeTests.Restore(catalog,game);}
        }
        [Test]
        public void StaticBoundaryStillAppliesToTheStepThroughAnEnemy()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(5,-9));var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].MovementBonus=5;
            state.Effects.Add(new ActiveEffect{Id="static",Kind=EffectKind.MovementBoundary,SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:3",ControllerSeat=3,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});game=new GameSession(catalog,codec,state);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves,Is.Empty);Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(6,-8)));
        }
        [TestCase(MoveMode.Secondary)] [TestCase(MoveMode.Fast)]
        public void ReplacingThePrimaryActionDoesNotCrossOrAttack(MoveMode mode)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);if(mode==MoveMode.Fast)Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:game.View(0).DebugTeleports["hero:0"].First(h=>catalog.Cell(h)!.Region=="blueFountain"));var options=mode==MoveMode.Fast?game.View(0).FastMoves:game.View(0).SecondaryMoves;Apply(game,0,CommandKind.Move,cell:options.First().Destination,mode:mode);Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared" || e.Kind=="EffectMoveChoiceRequired"),Is.False);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void MovementBonusCannotExtendTheTwoStepPassThrough()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].MovementBonus=5;game=new GameSession(catalog,codec,state);Begin(game);
            Assert.That(game.View(0).EffectMoves.All(m=>m.Path.Count==3),Is.True);Assert.That(game.View(0).EffectMoves,Is.Not.Empty);
        }
        [Test]
        public void CrystalVictoryDoesNotMoveOrAttackTwice()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);Begin(game);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-8));Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(1));Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(1));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void FrozenEngine28DoesNotGainPermissionToCrossUnits()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine28-onward-move.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Blink));Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-8));Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("attack_target"));ChargeTests.Restore(catalog,game);
        }
    }
}

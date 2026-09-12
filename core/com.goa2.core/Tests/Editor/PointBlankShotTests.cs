using System;
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
    public sealed class PointBlankShotTests
    {
        internal const string Shot="sabina-00-近身射击",Dodge="tigerclaw-18-躲闪";
        internal static GameSession Setup(ContentCatalog catalog,string ally="brogan")
        {
            var game=LocalGameFactory.Create(catalog,"pointblank",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"sabina,tigerclaw,"+ally+",arien");Apply(game,0,CommandKind.DebugEquipCard,Dodge,target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));
            string[] cards={Shot,"tigerclaw-07-伺机待发",ally=="wasp"?"wasp-07-抵挡屏障":"brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(0));return game;
        }
        private static GameSession Restore(ContentCatalog catalog,GameSession game)
        {string save=game.ExportSave();var result=LocalGameFactory.Restore(catalog,save);Assert.That(result.ExportSave(),Is.EqualTo(save));return result;}
        private static void Attack(GameSession game,string target="hero:1"){Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,target);}
        [Test]
        public void ExactContractAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Shot);Assert.That(card.PrimaryValue,Is.EqualTo(2));Assert.That(card.SubtypeValue,Is.EqualTo(1));Assert.That(card.Initiative,Is.EqualTo(12));
            Assert.That(card.PrimaryCategory,Is.EqualTo("基础攻击"));Assert.That(card.SecondaryMovement,Is.EqualTo(1));Assert.That(card.SecondaryDefense,Is.EqualTo(1));
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,21),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void SuccessfulNumericDefenseOrDodgeStillPushesExactlyOnce(bool block)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);game=Restore(catalog,game);string response=block?Dodge:"tigerclaw-02-偷袭";
            Assert.That(game.View(1).DefenseOptions.Single(d=>d.CardId==response).Assessment.Successful,Is.True);
            var defend=Cmd(game,1,CommandKind.Defend,response);Assert.That(game.Execute(1,defend).Accepted,Is.True);game=Restore(catalog,game);var view=game.View(0);
            Assert.That(view.Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(8,-8)));Assert.That(view.Players[0].Gold,Is.Zero);Assert.That(view.RedCrystal,Is.EqualTo(7));
            var push=view.Events.Single(e=>e.Kind=="UnitPushed");Assert.That(push.CardId,Is.EqualTo(Shot));Assert.That(push.Seat,Is.EqualTo(1));Assert.That(push.From,Is.EqualTo(new Hex(7,-8)));Assert.That(push.To,Is.EqualTo(new Hex(8,-8)));
            Assert.That(push.Path,Is.EqualTo(new[]{new Hex(7,-8),new Hex(8,-8)}));Assert.That(push.Detail,Does.Contain("by:0"));
            Assert.That(view.Events.First(e=>e.Kind=="DefenseResolved").Sequence,Is.LessThan(push.Sequence));Assert.That(view.Events.First(e=>e.Kind=="AttackResolved").Sequence,Is.LessThan(push.Sequence));Assert.That(push.Sequence,Is.LessThan(view.Events.First(e=>e.Kind=="CardResolved").Sequence));
            string after=game.ExportSave();Assert.That(game.Execute(1,defend).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
        }
        [TestCase(1,0)] [TestCase(0,1)] [TestCase(-1,1)] [TestCase(-1,0)] [TestCase(0,-1)] [TestCase(1,-1)]
        public void AllSixDirectionsPushDirectlyAway(int dx,int dy)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var occupied=game.View(0).Units.Select(u=>u.Position).ToList();
            var origin=catalog.Cells.First(c=>!c.Obstacle && !occupied.Contains(c.Position) &&
                catalog.Cell(new Hex(c.Position.X+dx,c.Position.Y+dy))?.Obstacle==false && catalog.Cell(new Hex(c.Position.X+2*dx,c.Position.Y+2*dy))?.Obstacle==false &&
                !occupied.Contains(new Hex(c.Position.X+dx,c.Position.Y+dy)) && !occupied.Contains(new Hex(c.Position.X+2*dx,c.Position.Y+2*dy))).Position;
            var target=new Hex(origin.X+dx,origin.Y+dy);var final=new Hex(origin.X+2*dx,origin.Y+2*dy);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:origin);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:target);Attack(game);Apply(game,1,CommandKind.Defend,Dodge);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(final));Restore(catalog,game);
        }
        [TestCase(false)] [TestCase(true)]
        public void TerrainOrMapEdgeStopsAtZeroWithoutFailingTheCard(bool edge)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var occupied=game.View(0).Units.Select(u=>u.Position).ToList();
            var pair=catalog.Cells.Where(c=>!c.Obstacle && !occupied.Contains(c.Position)).SelectMany(c=>c.Position.Neighbors().Select(t=>new{Source=c.Position,Target=t,End=new Hex(t.X*2-c.Position.X,t.Y*2-c.Position.Y)}))
                .First(p=>catalog.Cell(p.Target)?.Obstacle==false && !occupied.Contains(p.Target) && (edge?catalog.Cell(p.End)==null:catalog.Cell(p.End)?.Obstacle==true));
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:pair.Source);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:pair.Target);Attack(game);Apply(game,1,CommandKind.Defend,Dodge);game=Restore(catalog,game);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(pair.Target));Assert.That(game.View(0).Events.Single(e=>e.Kind=="UnitPushed").Path.Count,Is.EqualTo(1));
            Assert.That(game.View(0).Events.Single(e=>e.Kind=="PushStopped").Detail,Is.EqualTo(edge?"map_edge":"obstacle"));Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));
        }
        [TestCase(false)] [TestCase(true)]
        public void FriendlyHeroOrEnemyMinionOccupancyStopsPush(bool minion)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);string blocker=minion?game.View(0).Units.First(u=>u.Kind=="melee" && u.Team==Team.Red).Id:"hero:2";
            Apply(game,0,CommandKind.DebugTeleport,blocker,cell:new Hex(8,-8));Attack(game);Apply(game,1,CommandKind.Defend,Dodge);game=Restore(catalog,game);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(7,-8)));Assert.That(game.View(0).Events.Single(e=>e.Kind=="PushStopped").Detail,Is.EqualTo("occupied"));
            Assert.That(game.View(0).Units.Single(u=>u.Id==blocker).Position,Is.EqualTo(new Hex(8,-8)));
        }
        [TestCase(false)] [TestCase(true)]
        public void DefeatedHeroOrMinionIsNotPushed(bool minion)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);string target="hero:1";
            if(minion){target=game.View(0).Units.First(u=>u.Kind=="melee" && u.Team==Team.Red).Id;Apply(game,0,CommandKind.DebugTeleport,target,cell:new Hex(5,-8));}
            Attack(game,target);if(!minion)Apply(game,1,CommandKind.DeclineDefense);game=Restore(catalog,game);
            Assert.That(game.View(0).Units.Any(u=>u.Id==target),Is.False);Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitPushed"),Is.False);Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(minion?2:1));
        }
        [Test]
        public void ExtendedRangeDoesNotPushNonAdjacentTarget()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));var state=new JsonStateCodec().Read(game.ExportSave());
            state.Players[0].RangedBonus=1;var rules=new GameRules();rules.Apply(catalog,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});rules.Apply(catalog,state,new Command{ActorSeat=0,Kind=CommandKind.ChooseAttackTarget,Value="hero:1"});rules.Apply(catalog,state,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value=Dodge});
            Assert.That(state.Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(8,-8)));Assert.That(state.Events.Any(e=>e.Kind=="UnitPushed"),Is.False);
        }
        [Test]
        public void PushIgnoresMovementBonusAndStaticMovementBoundary()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,"wasp");Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(5,-8));Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());
            // Isolated rule state: an earlier Static Lock aura exists; its activation order is separately tested.
            state.Effects.Add(new ActiveEffect {Id="seed-static",Kind=EffectKind.MovementBoundary,SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:2",ControllerSeat=2,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            state.Players[0].MovementBonus=3;state.Players[1].MovementBonus=3;var target=state.Units.Single(u=>u.Seat==1);
            Assert.That(EffectRules.CanMoveAcross(catalog,state,target,target.Position,new Hex(8,-8)),Is.False);
            new GameRules().Apply(catalog,state,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value=Dodge});Assert.That(target.Position,Is.EqualTo(new Hex(8,-8)));Assert.That(state.Events.Single(e=>e.Kind=="UnitPushed").Path.Count,Is.EqualTo(2));
        }
        [Test]
        public void NoTargetAndFinalCrystalDoNotLeaveExtraWork()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));Apply(game,0,CommandKind.BeginPrimary);game=Restore(catalog,game);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));
            game=Setup(catalog);Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);Attack(game);Apply(game,1,CommandKind.DeclineDefense);game=Restore(catalog,game);Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitPushed"),Is.False);
        }
        [Test]
        public void FrozenStormRemainsRestorableAndFinishesItsOwnChoice()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine21-storm-removal.json"));var game=LocalGameFactory.Restore(catalog,save);
            Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Shot));Apply(game,0,CommandKind.ChooseEffectTarget,"minion:1,-1");game=Restore(catalog,game);Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));
        }
    }
}

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
    public sealed class MightyBlowTests
    {
        internal const string Blow="brogan-00-猛攻";
        internal static GameSession Setup(ContentCatalog catalog,string lastHero="arien",bool counter=false)
        {
            var game=LocalGameFactory.Create(catalog,"mighty",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,tigerclaw,sabina,"+lastHero);
            if(counter)Apply(game,0,CommandKind.DebugEquipCard,"tigerclaw-14-近身格挡",target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));
            string[] cards={Blow,"tigerclaw-07-伺机待发","sabina-07-指挥",lastHero=="wasp"?"wasp-07-抵挡屏障":"arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);return game;
        }
        private static GameSession Restore(ContentCatalog catalog,GameSession game)
        {var save=game.ExportSave();var result=LocalGameFactory.Restore(catalog,save);Assert.That(result.ExportSave(),Is.EqualTo(save));return result;}
        private static void Attack(GameSession game,string target="hero:1") {Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,target);}
        [Test]
        public void ExactContractAndVersionGate()
        {
            var card=BattlefieldTests.Catalog().Card(Blow);Assert.That(card.PrimaryValue,Is.EqualTo(3));Assert.That(card.Initiative,Is.EqualTo(11));Assert.That(card.PrimaryCategory,Is.EqualTo("基础攻击"));
            Assert.That(card.SecondaryMovement,Is.EqualTo(1));Assert.That(card.SecondaryDefense,Is.EqualTo(3));Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,22),Is.False);
            card.Text+="然后攻击一次。";Assert.That(CombatRules.HasPrimaryProgram(card),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void DefeatedHeroOrMinionVacatesTheRememberedSpace(bool minion)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);string target="hero:1";var destination=new Hex(7,-8);
            if(minion){target=game.View(0).Units.First(u=>u.Kind=="melee" && u.Team==Team.Red).Id;destination=new Hex(5,-8);Apply(game,0,CommandKind.DebugTeleport,target,cell:destination);}
            Attack(game,target);if(!minion){game=Restore(catalog,game);Apply(game,1,CommandKind.DeclineDefense);}game=Restore(catalog,game);var view=game.View(0);
            Assert.That(view.Units.Single(u=>u.Seat==0).Position,Is.EqualTo(destination));Assert.That(view.Units.Any(u=>u.Id==target),Is.False);Assert.That(view.Players[0].Gold,Is.EqualTo(minion?2:1));
            var moved=view.Events.Single(e=>e.Kind=="UnitMoved");Assert.That(moved.CardId,Is.EqualTo(Blow));Assert.That(moved.Seat,Is.EqualTo(0));Assert.That(moved.From,Is.EqualTo(new Hex(6,-8)));Assert.That(moved.To,Is.EqualTo(destination));Assert.That(moved.Path.Count,Is.EqualTo(2));
            Assert.That(view.Events.First(e=>e.Kind=="AttackResolved").Sequence,Is.LessThan(moved.Sequence));Assert.That(moved.Sequence,Is.LessThan(view.Events.First(e=>e.Kind=="CardResolved").Sequence));Assert.That(view.Pending,Is.Null);
        }
        [Test]
        public void SuccessfulDefenseKeepsOccupiedSpaceAndCompletesCard()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);game=Restore(catalog,game);Apply(game,1,CommandKind.Defend,"tigerclaw-02-偷袭");game=Restore(catalog,game);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(6,-8)));Assert.That(game.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(7,-8)));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);Assert.That(game.View(0).Events.Single(e=>e.Kind=="EffectMoveSkipped").Detail,Is.EqualTo("target_position_occupied"));Assert.That(game.View(0).ActiveSeat,Is.Not.EqualTo(0));
        }
        [Test]
        public void InvalidAndDuplicateDefenseCannotMoveAgain()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var before=game.ExportSave();var wrong=Cmd(game,2,CommandKind.DeclineDefense);Assert.That(game.Execute(2,wrong).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));
            var decline=Cmd(game,1,CommandKind.DeclineDefense);Assert.That(game.Execute(1,decline).Accepted,Is.True);var after=game.ExportSave();Assert.That(game.Execute(1,decline).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));Assert.That(game.View(0).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(1));Restore(catalog,game);
        }
        [Test]
        public void TargetsStayAdjacentEnemyOnlyAndProtectedHeavyIsExcluded()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(5,-8));var heavy=game.View(0).Units.Single(u=>u.Kind=="heavy" && u.Team==Team.Red).Id;Apply(game,0,CommandKind.DebugTeleport,heavy,cell:new Hex(6,-9));
            Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).AttackTargets,Is.EqualTo(new[]{"hero:1"}));var before=game.ExportSave();ApplyRejected(game,0,CommandKind.ChooseAttackTarget,"hero:2",before);ApplyRejected(game,0,CommandKind.ChooseAttackTarget,heavy,before);
        }
        private static void ApplyRejected(GameSession game,int seat,CommandKind kind,string value,string before)
        {Assert.That(game.Execute(seat,Cmd(game,seat,kind,value)).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
        [Test]
        public void MovementAndRangedBonusesDoNotAddAnExtraMoveChoice()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var state=new JsonStateCodec().Read(game.ExportSave());state.Players[0].MovementBonus=3;state.Players[0].RangedBonus=3;state.Players[0].RangeBonus=3;
            var rules=new GameRules();rules.Apply(catalog,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});Assert.That(CombatRules.AttackTargets(catalog,state,0),Is.EqualTo(new[]{"hero:1"}));rules.Apply(catalog,state,new Command{ActorSeat=0,Kind=CommandKind.ChooseAttackTarget,Value="hero:1"});rules.Apply(catalog,state,new Command{ActorSeat=1,Kind=CommandKind.DeclineDefense});
            Assert.That(state.Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(7,-8)));Assert.That(state.Pending,Is.Null);Assert.That(state.Events.Single(e=>e.Kind=="UnitMoved").Path.Count,Is.EqualTo(2));
        }
        [Test]
        public void StaticBoundaryPreventsNormalMovementAfterACompletedKill()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,"wasp");Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(5,-9));var state=new JsonStateCodec().Read(game.ExportSave());
            // Isolated rule state supplies an enemy Static Lock aura; activation and expiry have their own tests.
            state.Effects.Add(new ActiveEffect{Id="static",Kind=EffectKind.MovementBoundary,SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:3",ControllerSeat=3,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            var rules=new GameRules();rules.Apply(catalog,state,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});rules.Apply(catalog,state,new Command{ActorSeat=0,Kind=CommandKind.ChooseAttackTarget,Value="hero:1"});rules.Apply(catalog,state,new Command{ActorSeat=1,Kind=CommandKind.DeclineDefense});
            Assert.That(state.Units.Any(u=>u.Seat==1),Is.False);Assert.That(state.Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(6,-8)));Assert.That(state.Events.Any(e=>e.Kind=="UnitMoved"),Is.False);
        }
        [Test]
        public void NoTargetsAndVictoryLeaveNoExtraMovement()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);
            game=Setup(catalog);Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);Attack(game);Apply(game,1,CommandKind.DeclineDefense);Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);Restore(catalog,game);
        }
        [Test]
        public void DefenseWindowStoresTargetCellWithoutLeakingHandCards()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);game=Restore(catalog,game);
            var json=Newtonsoft.Json.Linq.JObject.Parse(game.ExportSave());Assert.That((int?)json["Execution"]!["AttackTargetCell"]?["X"],Is.EqualTo(7));Assert.That((int?)json["Execution"]!["AttackTargetCell"]?["Y"],Is.EqualTo(-8));
            Assert.That(game.View(0).DefenseOptions,Is.Empty);Assert.That(game.View(1).DefenseOptions,Is.Not.Empty);
        }
        [TestCase(false,false)] [TestCase(false,true)] [TestCase(true,false)] [TestCase(true,true)]
        public void HeavyProgressionRechecksOldTargetCellAfterAllSpawnChoices(bool newMinionOccupiesTarget,bool delayed)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var origin=new Hex(6,-9);var targetCell=newMinionOccupiesTarget?new Hex(5,-9):new Hex(6,-8);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:origin);
            foreach(var unit in game.View(0).Units.Where(u=>u.Team==Team.Red && u.Kind!="hero" && u.Kind!="heavy").ToList())Apply(game,0,CommandKind.DebugRemoveMinion,unit.Id);
            string heavy=game.View(0).Units.Single(u=>u.Team==Team.Red && u.Kind=="heavy").Id;Apply(game,0,CommandKind.DebugTeleport,heavy,cell:targetCell);
            if(delayed)Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(2,-7));Attack(game,heavy);
            if(delayed){Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("minion_spawn"));Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(origin));}
            while(game.View(0).Pending?.Kind=="minion_spawn")
            {
                game=Restore(catalog,game);int chooser=game.View(0).Pending!.ChooserSeat;var option=game.View(chooser).SpawnChoices.First(x=>x.Value.Count>0);Apply(game,chooser,CommandKind.ChooseMinionSpawn,option.Key,cell:option.Value.First());
            }
            game=Restore(catalog,game);var view=game.View(0);Assert.That(view.Units.Single(u=>u.Seat==0).Position,Is.EqualTo(newMinionOccupiesTarget?origin:targetCell));Assert.That(view.Players[0].Gold,Is.EqualTo(4));Assert.That(view.BlueMarks,Is.EqualTo(1));
            Assert.That(view.Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));Assert.That(view.Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(newMinionOccupiesTarget?0:1));Assert.That(view.Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));
            if(newMinionOccupiesTarget)Assert.That(view.Units.Any(u=>u.Kind!="hero" && u.Position==targetCell),Is.True);
        }
        [Test]
        public void MeleeBlockCounterWaitsUntilFailedMovementHasBeenResolved()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,counter:true);Attack(game);Apply(game,1,CommandKind.Defend,"tigerclaw-14-近身格挡");game=Restore(catalog,game);
            Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("forced_discard"));Assert.That(game.View(0).Pending!.ChooserSeat,Is.EqualTo(0));Assert.That(game.View(0).Events.Any(e=>e.Kind=="EffectMoveSkipped"),Is.True);Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);
            Apply(game,0,CommandKind.ForcedDiscard,"brogan-01-冲撞");game=Restore(catalog,game);Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));
        }
        [Test]
        public void FrozenPointBlankDefenseKeepsOriginalBytesAndPushBehavior()
        {
            var catalog=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine22-pointblank-defense.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));
            Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Blow));Apply(game,1,CommandKind.Defend,"tigerclaw-02-偷袭");game=Restore(catalog,game);Assert.That(game.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(8,-8)));
        }
    }
}

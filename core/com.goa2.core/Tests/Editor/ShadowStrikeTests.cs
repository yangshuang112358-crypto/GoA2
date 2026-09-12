using System;
using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class ShadowStrikeTests
    {
        internal const string Shadow="tigerclaw-04-影袭";
        internal static GameSession Setup(ContentCatalog catalog,string card=Shadow,string defender="wasp",int engine=GameState.CurrentEngineVersion)
        {
            var game=SneakAttackTests.Setup(catalog,defender,engine);
            Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:game.View(0).DebugTeleports["hero:3"].First(h=>h.Distance(new Hex(7,-10))>5));
            return game;
        }
        internal static void Select(GameSession game,ContentCatalog catalog,string card=Shadow)
        {
            for(int seat=0;seat<4;seat++)
            {
                string id=seat==0 ? card : game.View(seat).OwnCards.Select(c=>catalog.Card(c.CardId))
                    .Where(c=>c.PrimaryFamily!="defense" && c.Color!="gold").OrderBy(c=>c.Initiative).ThenBy(c=>c.Id,StringComparer.Ordinal).First().Id;
                Apply(game,seat,CommandKind.SelectCard,id);
            }
            Assert.That(game.View(null).ActiveSeat,Is.EqualTo(0));
        }
        private static void AssertRestores(ContentCatalog catalog,ref GameSession game)
        {
            string save=game.ExportSave();game=LocalGameFactory.Restore(catalog,save);
            Assert.That(game.ExportSave(),Is.EqualTo(save));
        }
        [Test]
        public void ExactCardDataAndVersionGate()
        {
            var card=BattlefieldTests.Catalog().Card(Shadow);
            Assert.That(card.PrimaryValue,Is.EqualTo(4));Assert.That(card.Initiative,Is.EqualTo(9));
            Assert.That(card.SecondaryMovement,Is.EqualTo(5));Assert.That(card.SecondaryDefense,Is.EqualTo(4));
            Assert.That(card.Subtype,Is.Null);Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);
            Assert.That(CombatRules.HasPrimaryProgram(card,12),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void OnlyActuallyMovingBeforeAttackConsumesTheLaterMove(bool moveBefore)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Select(game,catalog);
            Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("effect_move"));
            Assert.That(game.View(0).Pending!.ResumeAt,Is.EqualTo("before_attack"));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);
            AssertRestores(catalog,ref game);
            Apply(game,0,CommandKind.ChooseEffectMove,moveBefore ? "" : "skip",cell:new Hex(7,-9));
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("attack_target"));AssertRestores(catalog,ref game);
            Assert.That(JObject.Parse(game.ExportSave())["Execution"]!["PreAttackMoved"]?.Value<bool>() ?? false,Is.EqualTo(moveBefore));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");AssertRestores(catalog,ref game);
            Apply(game,1,CommandKind.DeclineDefense);
            if(!moveBefore)
            {
                Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("effect_move"));AssertRestores(catalog,ref game);
                Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-10));
            }
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution,Is.Null);
            Assert.That(game.View(null).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(moveBefore ? new Hex(7,-9) : new Hex(8,-10)));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(1));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="CardResolved" && e.Seat==0),Is.EqualTo(1));AssertRestores(catalog,ref game);
        }
        [Test]
        public void CanApproachAnInitiallyOutOfReachTargetAndRecomputesLegality()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));Select(game,catalog);
            Assert.That(game.View(0).AttackTargets,Does.Not.Contain("hero:1"));Assert.That(game.View(0).CanBeginPrimary,Is.True);
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-10));
            Assert.That(game.View(0).AttackTargets,Does.Contain("hero:1"));
            Assert.That(game.View(0).AttackTargets,Does.Not.Contain("hero:2"));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="EffectMoveChoiceRequired"),Is.EqualTo(1));
        }
        [TestCase(false)] [TestCase(true)]
        public void NoTargetStopsAfterTheOptionalPreMoveWithoutFreePostMove(bool move)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:game.View(0).DebugTeleports["hero:1"].First(h=>h.Distance(new Hex(7,-10))>4));
            Select(game,catalog);Apply(game,0,CommandKind.BeginPrimary);
            Apply(game,0,CommandKind.ChooseEffectMove,move ? "" : "skip",cell:new Hex(8,-10));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="EffectMoveChoiceRequired"),Is.EqualTo(1));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="CardEffectStopped" && e.Detail=="no_targets"),Is.True);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(move ? new Hex(8,-10) : new Hex(7,-10)));
            AssertRestores(catalog,ref game);
        }
        [Test]
        public void ChoicesArePrivateAtomicAndIdempotentAcrossRestore()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Select(game,catalog);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(1).EffectMoves,Is.Empty);Assert.That(game.View(null).EffectMoves,Is.Empty);
            string before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,1,CommandKind.ChooseEffectMove,"skip"),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(8,-10)),
                Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(8,-9)),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(7,-9),mode:MoveMode.Fast),Cmd(game,0,CommandKind.Pass)})
            { Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before)); }
            var valid=Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(7,-9));Assert.That(game.Execute(0,valid).Accepted,Is.True);
            AssertRestores(catalog,ref game);string after=game.ExportSave();
            Assert.That(game.Execute(0,valid).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseEffectMove,"skip")).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(after));
        }
        [TestCase(false)] [TestCase(true)]
        public void SuccessfulDefenseStillHonorsTheSavedMovementCondition(bool moveBefore)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,defender:"arien");
            Apply(game,0,CommandKind.DebugEquipCard,"arien-13-挑战者",target:1);Select(game,catalog);Apply(game,0,CommandKind.BeginPrimary);
            Apply(game,0,CommandKind.ChooseEffectMove,moveBefore ? "" : "skip",cell:new Hex(7,-9));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.Defend,"arien-13-挑战者");
            Assert.That(game.View(0).Players[1].AwaitingRespawn,Is.False);
            if(!moveBefore){Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("effect_move"));Apply(game,0,CommandKind.ChooseEffectMove,"skip");}
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution,Is.Null);AssertRestores(catalog,ref game);
        }
        [Test]
        public void RealEngine12RecoverySaveKeepsItsExactDerivedBytesAndContinues()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine12-loyal-choice.json"));
            var codec=new JsonStateCodec();Assert.That(codec.Write(codec.Read(save)),Is.EqualTo(save));
            var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));
            Apply(game,0,CommandKind.ChooseRecoveredCard,"shargatha-01-劈砍");
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(game,0,CommandKind.Defend,"shargatha-01-劈砍");
            Assert.That(game.View(0).EngineVersion,Is.EqualTo(12));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Shadow));
            Assert.That(game.View(0).Players[0].AwaitingRespawn,Is.False);AssertRestores(catalog,ref game);
        }
        [Test]
        public void NoAvailablePreMoveDoesNotConsumeTheMoveAfterDefeatingABlockingHero()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var origin=new Hex(7,-10);
            foreach(var cell in catalog.Cells.Where(c=>!c.Obstacle && c.Position.Distance(origin)==1).Select(c=>c.Position))
            {
                if(game.View(null).Units.Any(u=>u.Position==cell)) continue;
                string minion=game.View(null).Units.First(u=>u.Kind=="melee" && u.Position.Distance(origin)>2).Id;
                Apply(game,0,CommandKind.DebugTeleport,minion,cell:cell);
            }
            Select(game,catalog);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("attack_target"));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="EffectMoveSkipped" && e.Detail=="no_destinations"),Is.True);
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("effect_move"));
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-10));AssertRestores(catalog,ref game);
        }
        [Test]
        public void FixedDistanceIgnoresMovementBonusAndMinionAttackStillCompletesOnce()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            string minion=game.View(null).Units.First(u=>u.Kind=="melee" && u.Team==Team.Red).Id;
            Apply(game,0,CommandKind.DebugTeleport,minion,cell:new Hex(8,-9));
            Select(game,catalog);Apply(game,0,CommandKind.BeginPrimary);
            var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].MovementBonus=4;
            var seeded=new GameSession(catalog,codec,state);
            Assert.That(seeded.View(0).EffectMoves.All(m=>m.Path.Count==2),Is.True);
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(7,-9));Apply(game,0,CommandKind.ChooseAttackTarget,minion);
            Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));Assert.That(game.View(0).Units.Any(u=>u.Id==minion),Is.False);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution,Is.Null);AssertRestores(catalog,ref game);
        }
        [TestCase(false)] [TestCase(true)]
        public void ExistingAuraConstrainsBothMoveWindowsButDoesNotSuppressAttack(bool silence)
        {
            var catalog=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(catalog,"shadow-aura",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,silence ? "arien,tigerclaw,brogan,wasp" : "wasp,tigerclaw,brogan,arien");
            Apply(game,0,CommandKind.DebugEquipCard,Shadow,target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-10));
            string[] cards={silence ? "arien-06-打断施法" : "wasp-06-静电封锁",Shadow,"brogan-06-铜墙铁壁",silence ? "wasp-07-抵挡屏障" : "arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(1).CanBeginPrimary,Is.True);
            Apply(game,1,CommandKind.BeginPrimary);
            Assert.That(game.View(1).EffectMoves.Any(m=>m.Destination==new Hex(8,-8)),Is.EqualTo(silence));
            Apply(game,1,CommandKind.ChooseEffectMove,"skip");Apply(game,1,CommandKind.ChooseAttackTarget,"hero:2");Apply(game,2,CommandKind.DeclineDefense);
            Assert.That(game.View(1).EffectMoves.Any(m=>m.Destination==new Hex(8,-8)),Is.EqualTo(silence));
            AssertRestores(catalog,ref game);Apply(game,1,CommandKind.ChooseEffectMove,"skip");AssertRestores(catalog,ref game);
        }
        [Test]
        public void CrystalVictoryEndsImmediatelyWithoutTheConditionalMove()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);Select(game,catalog);
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,"skip");Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(game.View(0).Pending,Is.Null);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="EffectMoveChoiceRequired"),Is.EqualTo(1));AssertRestores(catalog,ref game);
        }
    }
}

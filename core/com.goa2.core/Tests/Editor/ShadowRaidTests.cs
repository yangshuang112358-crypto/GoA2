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
    public sealed class ShadowRaidTests
    {
        internal const string Raid="tigerclaw-06-暗影奇袭";
        [Test]
        public void ExactDataIsNewlyEnabledWithoutChangingShadowStrike()
        {
            var catalog=BattlefieldTests.Catalog();var card=catalog.Card(Raid);
            Assert.That(card.Initiative,Is.EqualTo(10));Assert.That(card.PrimaryValue,Is.EqualTo(4));
            Assert.That(card.SecondaryMovement,Is.EqualTo(5));Assert.That(card.SecondaryDefense,Is.EqualTo(4));Assert.That(card.Subtype,Is.Null);
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,13),Is.False);
            Assert.That(CombatRules.HasPrimaryProgram(catalog.Card(ShadowStrikeTests.Shadow),13),Is.True);
        }
        [TestCase(false,false)] [TestCase(false,true)] [TestCase(true,false)] [TestCase(true,true)]
        public void PreAndPostMovementAreIndependentAndRestoreAtEveryChoice(bool before,bool after)
        {
            var catalog=BattlefieldTests.Catalog();var game=ShadowStrikeTests.Setup(catalog,Raid);ShadowStrikeTests.Select(game,catalog,Raid);
            Apply(game,0,CommandKind.BeginPrimary);game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,0,CommandKind.ChooseEffectMove,before ? "" : "skip",cell:new Hex(7,-9));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("effect_move"));Assert.That(game.View(0).Pending!.ResumeAt,Is.EqualTo("card_text_move"));
            Assert.That(game.View(1).EffectMoves,Is.Empty);game=LocalGameFactory.Restore(catalog,game.ExportSave());
            string beforeChoice=game.ExportSave();var invalid=Cmd(game,1,CommandKind.ChooseEffectMove,"skip");
            Assert.That(game.Execute(1,invalid).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(beforeChoice));
            var command=Cmd(game,0,CommandKind.ChooseEffectMove,after ? "" : "skip",destination:new Hex(8,-10));
            Assert.That(game.Execute(0,command).Accepted,Is.True);string saved=game.ExportSave();game=LocalGameFactory.Restore(catalog,saved);
            Assert.That(game.Execute(0,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(saved));
            var view=game.View(null);
            Assert.That(view.Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo((before?1:0)+(after?1:0)));
            Assert.That(view.Events.Count(e=>e.Kind=="EffectMoveChoiceRequired"),Is.EqualTo(2));
            Assert.That(view.Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(view.Events.Count(e=>e.Kind=="CardResolved" && e.Seat==0),Is.EqualTo(1));
            Assert.That(view.Players[0].Gold,Is.EqualTo(1));Assert.That(view.Players[1].AwaitingRespawn,Is.True);
        }
        [Test]
        public void NumericDefenseDoesNotPreventTheSecondMove()
        {
            var catalog=BattlefieldTests.Catalog();var game=ShadowStrikeTests.Setup(catalog,Raid,"arien");
            Apply(game,0,CommandKind.DebugEquipCard,"arien-13-挑战者",target:1);ShadowStrikeTests.Select(game,catalog,Raid);
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(7,-9));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.Defend,"arien-13-挑战者");
            Assert.That(game.View(0).Players[1].AwaitingRespawn,Is.False);
            Assert.That(game.View(0).EffectMoves.Any(m=>m.Destination==new Hex(8,-10)),Is.False,"The defender still occupies the target cell.");
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(7,-10));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(2));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void PreMoveWithoutAnyAttackTargetCannotGiveASecondMove()
        {
            var catalog=BattlefieldTests.Catalog();var game=ShadowStrikeTests.Setup(catalog,Raid);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:game.View(0).DebugTeleports["hero:1"].First(h=>h.Distance(new Hex(7,-10))>4));
            ShadowStrikeTests.Select(game,catalog,Raid);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-10));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="EffectMoveChoiceRequired"),Is.EqualTo(1));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution,Is.Null);
        }
        [Test]
        public void SecondaryMovementDoesNotRunEitherTextStep()
        {
            var catalog=BattlefieldTests.Catalog();var game=ShadowStrikeTests.Setup(catalog,Raid);ShadowStrikeTests.Select(game,catalog,Raid);
            Apply(game,0,CommandKind.Move,cell:game.View(0).SecondaryMoves.First().Destination);
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="EffectMoveChoiceRequired" || e.Kind=="AttackDeclared"),Is.False);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Raid).Zone,Is.EqualTo(CardZone.PlayedResolved));
        }
        [Test]
        public void FrozenEngine13ShadowFactStaysTrueAndDoesNotGainAnotherMove()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine13-shadow-target.json"));
            var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));
            Assert.That(new JsonStateCodec().Read(save).Execution!.PreAttackMoved,Is.True);
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).EngineVersion,Is.EqualTo(13));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Raid));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="EffectMoveChoiceRequired"),Is.EqualTo(1));
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution,Is.Null);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void StaticFieldKeepsBothMovementChoicesInsideTheBoundary()
        {
            var catalog=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(catalog,"raid-static",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"wasp,tigerclaw,brogan,arien");Apply(game,0,CommandKind.DebugEquipCard,Raid,target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-10));
            string[] cards={"wasp-06-静电封锁",Raid,"brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,1,CommandKind.BeginPrimary);
            Assert.That(game.View(1).EffectMoves.Any(m=>m.Destination==new Hex(8,-8)),Is.False);
            Apply(game,1,CommandKind.ChooseEffectMove,"skip");Apply(game,1,CommandKind.ChooseAttackTarget,"hero:2");Apply(game,2,CommandKind.DeclineDefense);
            Assert.That(game.View(1).EffectMoves.Any(m=>m.Destination==new Hex(8,-8)),Is.False);
            string before=game.ExportSave();Assert.That(game.Execute(1,Cmd(game,1,CommandKind.ChooseEffectMove,destination:new Hex(8,-8))).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));Apply(game,1,CommandKind.ChooseEffectMove,cell:new Hex(8,-10));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void DefeatingTheLastCrystalPreventsTheSecondMovement()
        {
            var catalog=BattlefieldTests.Catalog();var game=ShadowStrikeTests.Setup(catalog,Raid);
            Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);ShadowStrikeTests.Select(game,catalog,Raid);
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(7,-9));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(game.View(0).Pending,Is.Null);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(1));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="EffectMoveChoiceRequired"),Is.EqualTo(1));
        }
    }
}

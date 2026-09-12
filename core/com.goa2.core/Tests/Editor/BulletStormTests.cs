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
    public sealed class BulletStormTests
    {
        internal const string Storm="sabina-04-枪林弹雨";
        private static GameSession Setup(ContentCatalog catalog)
        {var game=CrossfireTests.Setup(catalog,Storm);Apply(game,0,CommandKind.DebugTeleport,CrossfireTests.Extra(game),cell:new Hex(6,-7));return game;}
        private static GameSession Restore(ContentCatalog catalog,GameSession game)
        {string save=game.ExportSave();var result=LocalGameFactory.Restore(catalog,save);Assert.That(result.ExportSave(),Is.EqualTo(save));return result;}
        private static void Attack(GameSession game,string target=CrossfireTests.First){Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,target);}
        [Test]
        public void ExactContractAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Storm);Assert.That(card.PrimaryValue,Is.EqualTo(3));Assert.That(card.SubtypeValue,Is.EqualTo(2));Assert.That(card.Initiative,Is.EqualTo(9));
            Assert.That(card.SecondaryMovement,Is.EqualTo(4));Assert.That(card.SecondaryDefense,Is.EqualTo(5));Assert.That(card.Passive,Is.EqualTo("范围"));
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,20),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void RemovesAtDistanceTwoOrSkipsWithoutExtraReward(bool skip)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);string extra=CrossfireTests.Extra(game);Attack(game);game=Restore(catalog,game);
            Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("effect_minion"));Assert.That(game.View(0).Pending!.Optional,Is.True);Assert.That(game.View(0).EffectTargets,Is.EqualTo(new[]{extra}));
            var choose=Cmd(game,0,CommandKind.ChooseEffectTarget,skip?"skip":extra);Assert.That(game.Execute(0,choose).Accepted,Is.True);game=Restore(catalog,game);
            string save=game.ExportSave();Assert.That(game.Execute(0,choose).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(save));var view=game.View(0);
            Assert.That(view.Units.Any(u=>u.Id==extra),Is.EqualTo(skip));Assert.That(view.Players[0].Gold,Is.EqualTo(2));Assert.That(view.Players[2].Gold,Is.Zero);Assert.That(view.ActiveSeat,Is.EqualTo(2));
            Assert.That(view.Events.Count(e=>e.Kind=="MinionDefeated"),Is.EqualTo(1));Assert.That(view.Events.Count(e=>e.Kind=="MinionRemoved"),Is.EqualTo(skip?0:1));Assert.That(view.Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
        }
        [TestCase(6,-7,0,0,true)] [TestCase(6,-6,0,0,false)] [TestCase(6,-6,1,0,true)] [TestCase(6,-6,0,2,false)]
        public void ExtraRangeUsesRangedBonusAndNeverSkillRange(int x,int y,int ranged,int area,bool legal)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());string extra=CrossfireTests.Extra(game);
            state.Units.Single(u=>u.Seat==1).Position=new Hex(8,-5);state.Players[0].RangedBonus=ranged;state.Players[0].RangeBonus=area;state.Units.Single(u=>u.Id==extra).Position=new Hex(x,y);
            Assert.That(GameRules.LegalEffectTargets(catalog,state,0).Contains(extra),Is.EqualTo(legal));
        }
        [Test]
        public void ExtendedAttackRangeAlsoIncludesMoreBlockingEnemyHeroes()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Is.Not.Empty);state.Players[0].RangedBonus=1;Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Is.Empty);
        }
        [Test]
        public void EnemyOwnershipHeavyExclusionAndWrongSeatStayEnforced()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);string heavy=game.View(0).Units.Single(u=>u.Team==Team.Red && u.Kind=="heavy").Id;
            Apply(game,0,CommandKind.DebugTeleport,heavy,cell:new Hex(4,-7));Attack(game);string before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,1,CommandKind.ChooseEffectTarget,"skip"),Cmd(game,0,CommandKind.ChooseEffectTarget,"hero:2"),Cmd(game,0,CommandKind.ChooseEffectTarget,heavy)})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            Assert.That(game.View(1).EffectTargets,Is.Empty);Apply(game,0,CommandKind.ChooseEffectTarget,"skip");Restore(catalog,game);
        }
        [TestCase(false)] [TestCase(true)]
        public void HeroAttackDoesNotEnableExtraRemoval(bool defend)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Attack(game,"hero:1");
            if(defend)Apply(game,1,CommandKind.Defend,"tigerclaw-18-躲闪");else Apply(game,1,CommandKind.DeclineDefense);
            game=Restore(catalog,game);Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));
        }
        [Test]
        public void NoRemainingMinionDoesNotLeaveAnEmptyChoice()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,CrossfireTests.Extra(game),cell:new Hex(6,-6));Attack(game);
            game=Restore(catalog,game);Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));
        }
        [Test]
        public void HeavyDefeatCanRemoveDistanceTwoNewRegionMinionWithoutReward()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            foreach(var unit in game.View(0).Units.Where(u=>u.Team==Team.Red && u.Kind!="hero" && u.Kind!="heavy").ToList())Apply(game,0,CommandKind.DebugRemoveMinion,unit.Id);
            string heavy=game.View(0).Units.Single(u=>u.Team==Team.Red && u.Kind=="heavy").Id;Apply(game,0,CommandKind.DebugTeleport,heavy,cell:new Hex(6,-8));Attack(game,heavy);game=Restore(catalog,game);
            Assert.That(game.View(0).EffectTargets,Does.Contain("minion:1:4,-6"));Apply(game,0,CommandKind.ChooseEffectTarget,"minion:1:4,-6");game=Restore(catalog,game);
            Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(4));Assert.That(game.View(0).BlueMarks,Is.EqualTo(1));Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
        }
        [Test]
        public void FrozenCrossfireKeepsAdjacentTargetsAndExactSaveBytes()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine20-crossfire-removal.json"));var game=LocalGameFactory.Restore(catalog,save);
            Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Storm));
            var state=new JsonStateCodec().Read(save);string extra=CrossfireTests.Extra(game);state.Units.Single(u=>u.Id==extra).Position=new Hex(6,-7);Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Does.Not.Contain(extra));
            Apply(game,0,CommandKind.ChooseEffectTarget,extra);game=Restore(catalog,game);Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));
        }
    }
}

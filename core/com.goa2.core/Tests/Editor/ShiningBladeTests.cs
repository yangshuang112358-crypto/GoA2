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
    public sealed class ShiningBladeTests
    {
        internal static GameSession Setup(ContentCatalog catalog,bool silenceFirst=true,int engineVersion=GameState.CurrentEngineVersion)
        {
            var game=LocalGameFactory.Create(catalog,"shining-blade",new[] {"A","B","C","D"},42,true,engineVersion);
            Apply(game,0,CommandKind.DebugPrepare,"wasp,arien,brogan,sabina");
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));
            Apply(game,0,CommandKind.DebugSetCoin,"red");
            string[] cards={"wasp-00-闪耀之刃",silenceFirst ? "arien-06-打断施法" : "arien-07-潮水","brogan-06-铜墙铁壁","sabina-07-指挥"};
            for(int seat=0;seat<4;seat++) Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            if (silenceFirst) { Assert.That(game.View(null).ActiveSeat, Is.EqualTo(1)); Apply(game,1,CommandKind.BeginPrimary); }
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(0)); return game;
        }
        [Test]
        public void SuccessfulDefenseStillCancelsTheAdjacentSkillAndCreatesTheAttackSourcedSilence()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog);
            Assert.That(game.View(0).CanBeginPrimary, Is.True, "Skill suppression cannot block a basic attack");
            var begin=Cmd(game,0,CommandKind.BeginPrimary);
            Assert.That(game.Execute(0,begin).Accepted, Is.True); Assert.That(game.Execute(0,begin).Duplicate, Is.True);
            Assert.That(game.View(0).AttackTargets, Is.EqualTo(new[] {"hero:1"}));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(null).Attack!.BaseAttack, Is.EqualTo(3)); Assert.That(game.View(null).Attack!.Ranged, Is.False);
            game=LocalGameFactory.Restore(catalog,game.ExportSave());
            var defend=Cmd(game,1,CommandKind.Defend,"arien-13-挑战者");
            Assert.That(game.Execute(1,defend).Accepted, Is.True); string saved=game.ExportSave();
            Assert.That(game.Execute(1,defend).Duplicate, Is.True); Assert.That(game.ExportSave(), Is.EqualTo(saved));
            var effect=game.View(null).Effects.Single();
            Assert.That(effect.SourceCardId, Is.EqualTo("wasp-00-闪耀之刃")); Assert.That(effect.Kind, Is.EqualTo(EffectKind.SkillSuppression));
            Assert.That(effect.AreaKind, Is.EqualTo(EffectAreaKind.Adjacent)); Assert.That(effect.CreationOrder, Is.EqualTo(2));
            Assert.That(game.View(null).Events.Count(e => e.Kind=="EffectCancelled"), Is.EqualTo(1));
            Assert.That(game.View(null).Units.Any(u => u.Id=="hero:1"), Is.True);
            Assert.That(LocalGameFactory.Restore(catalog,saved).ExportSave(), Is.EqualTo(saved));
        }
        [Test]
        public void BasicAttackAllowsOnlyAdjacentEnemyHeroesEvenWithBothRangeBonuses()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog,false); var state=new JsonStateCodec().Read(game.ExportSave());
            state.Players[0].RangeBonus=20; state.Players[0].RangedBonus=20;
            state.Units.Single(u => u.Seat==2).Position=new Hex(7,-9);
            state.Units.Single(u => u.Seat==3).Position=new Hex(8,-9);
            state.Units.First(u => u.Kind=="melee" && u.Team==Team.Red).Position=new Hex(6,-9);
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Is.EqualTo(new[] {"hero:1"}));
            state.Units.Single(u => u.Seat==1).Position=new Hex(6,-8);
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Is.Empty);
        }
        [Test]
        public void AnAttackWithNoExistingSkillStillCreatesItsIndependentTurnRestriction()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog,false);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1"); Apply(game,1,CommandKind.Defend,"arien-13-挑战者");
            Assert.That(game.View(null).Events.Any(e => e.Kind=="EffectCancelled"), Is.False);
            Assert.That(game.View(null).Effects.Single().SourceCardId, Is.EqualTo("wasp-00-闪耀之刃"));
            Apply(game,0,CommandKind.DebugAdvance,"turn");
            Assert.That(game.View(null).Effects, Is.Empty); Assert.That(game.View(null).Turn, Is.EqualTo(2));
        }
        [Test]
        public void ADefeatedSourceIsNoLongerAdjacentWhenAfterAttackCancellationRuns()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1"); Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(null).Effects.Count, Is.EqualTo(2));
            Assert.That(game.View(null).Effects.Any(e => e.SourceCardId=="arien-06-打断施法"), Is.True);
            Assert.That(game.View(null).EffectAreas["effect:1"], Is.Empty);
            Assert.That(game.View(null).Events.Any(e => e.Kind=="EffectCancelled"), Is.False);
            Assert.That(game.View(null).Players[1].AwaitingRespawn, Is.True); Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void NoAttackTargetEndsTheCardWithoutApplyingItsLaterText()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog,false);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));
            Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(null).Effects, Is.Empty); Assert.That(game.View(null).Events.Last(e => e.Kind=="CardEffectStopped").Detail, Is.EqualTo("no_targets"));
        }
        [Test]
        public void VersionTwoAndNonPrimaryUseKeepTheirExistingBehavior()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog,false,2);
            Assert.That(game.View(0).PrimarySupported, Is.False); string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.BeginPrimary)).Code, Is.EqualTo("primary_not_implemented")); Assert.That(game.ExportSave(), Is.EqualTo(before));
            game=Setup(catalog,false); Apply(game,0,CommandKind.Move,cell:new Hex(7,-9));
            Assert.That(game.View(null).Effects, Is.Empty);
            game=CombatFlowTests.Duel(catalog); Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1"); Apply(game,1,CommandKind.Defend,"wasp-00-闪耀之刃");
            Assert.That(game.View(null).Effects, Is.Empty);
        }
    }
}

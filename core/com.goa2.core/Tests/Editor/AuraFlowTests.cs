using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class AuraFlowTests
    {
        internal static GameSession Setup(ContentCatalog catalog, bool silence=false, bool againstAttack=false, int engineVersion=GameState.CurrentEngineVersion)
        {
            string heroes=silence ? (againstAttack ? "arien,sabina,brogan,wasp" : "arien,wasp,brogan,sabina") : "wasp,sabina,brogan,arien";
            var game=LocalGameFactory.Create(catalog,"auras",new[] {"A","B","C","D"},42,true,engineVersion);
            Apply(game,0,CommandKind.DebugPrepare,heroes);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            string[] cards=silence ?
                (againstAttack ? new[] {"arien-06-打断施法","sabina-01-拔枪","brogan-06-铜墙铁壁","wasp-07-抵挡屏障"} : new[] {"arien-06-打断施法","wasp-06-静电封锁","brogan-06-铜墙铁壁","sabina-07-指挥"}) :
                new[] {"wasp-06-静电封锁","sabina-01-拔枪","brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int seat=0;seat<4;seat++) Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(0));
            return game;
        }
        [TestCase(29)] [TestCase(30)]
        public void PublicAreaProjectionTracksTheSourceAndCannotMutateAuthority(int engine)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog,engineVersion:engine); Apply(game,0,CommandKind.BeginPrimary);
            var view=game.View(null); var effect=view.Effects.Single(); var source=view.Units.Single(u => u.Id==effect.SourceUnitId);
            Assert.That(view.EffectAreas.ContainsKey(effect.Id), Is.True);
            Assert.That(view.EffectAreas[effect.Id], Is.EquivalentTo(catalog.Cells.Where(c => c.Position.Distance(source.Position)<=2).Select(c => c.Position)));
            view.EffectAreas[effect.Id].Clear(); view.Effects[0].Window.EndTurn=4;
            Assert.That(game.View(null).EffectAreas[effect.Id], Is.Not.Empty); Assert.That(game.View(null).Effects[0].Window.EndTurn, Is.EqualTo(1));
            Apply(game,0,CommandKind.DebugDefeatHero,effect.SourceUnitId,target:1);
            Assert.That(game.View(null).Effects.Count, Is.EqualTo(engine<30 ? 1 : 0));
            foreach(var checkedGame in new[]{game,LocalGameFactory.Restore(catalog,game.ExportSave())})
                if(engine<30) Assert.That(checkedGame.View(null).EffectAreas[effect.Id], Is.Empty);
                else Assert.That(checkedGame.View(null).EffectAreas.ContainsKey(effect.Id), Is.False);
        }
        [Test]
        public void StaticFieldStartsOnMainActionHasSourcesAndExpiresAtTheTurnBoundary()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog);
            Assert.That(game.View(null).Effects, Is.Empty); Assert.That(game.View(0).CanBeginPrimary, Is.True);
            Apply(game,0,CommandKind.BeginPrimary);
            var effect=game.View(null).Effects.Single();
            Assert.That(effect.Kind, Is.EqualTo(EffectKind.MovementBoundary)); Assert.That(effect.SourceCardId, Is.EqualTo("wasp-06-静电封锁"));
            Assert.That(effect.SourceUnitId, Is.EqualTo("hero:0")); Assert.That(effect.ControllerSeat, Is.EqualTo(0)); Assert.That(effect.CreationOrder, Is.EqualTo(1));
            Assert.That(effect.Window.StartRound, Is.EqualTo(1)); Assert.That(effect.Window.StartTurn, Is.EqualTo(1)); Assert.That(effect.Window.EndTurn, Is.EqualTo(1));
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(1));
            game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,0,CommandKind.DebugAdvance,"turn");
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.Planning)); Assert.That(game.View(null).Turn, Is.EqualTo(2));
            Assert.That(game.View(null).Effects, Is.Empty); Assert.That(game.View(null).Events.Count(e => e.Kind=="EffectExpired"), Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void StaticFieldRejectsLeavingItsCircleAndFollowsItsSourceWhenDebugMoved()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog); Apply(game,0,CommandKind.BeginPrimary);
            var source=game.View(null).Units.Single(u => u.Seat==0); var target=game.View(null).Units.Single(u => u.Seat==1);
            var outside=catalog.Cells.First(c => !c.Obstacle && c.Position.Distance(target.Position)==1 && c.Position.Distance(source.Position)>2 && !game.View(null).Units.Any(u => u.Position==c.Position)).Position;
            Assert.That(game.View(1).SecondaryMoves.Any(o => o.Destination==outside), Is.False);
            Assert.That(game.View(1).SecondaryMoves.All(o => o.Path.All(h => h.Distance(source.Position)<=2)), Is.True);
            string before=game.ExportSave();
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.Move,destination:outside)).Code, Is.EqualTo("invalid_move")); Assert.That(game.ExportSave(), Is.EqualTo(before));
            var far=catalog.Cells.First(c => !c.Obstacle && c.Position.Distance(target.Position)>6 && !game.View(null).Units.Any(u => u.Position==c.Position)).Position;
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:far);
            Assert.That(game.View(1).SecondaryMoves.Any(o => o.Destination==outside), Is.True);
        }
        [Test]
        public void SilenceBlocksTheEnemySkillWithoutConsumingItsCard()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog,silence:true); Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(1)); Assert.That(game.View(1).PrimarySupported, Is.True);
            Assert.That(game.View(1).CanBeginPrimary, Is.False); Assert.That(game.View(1).PrimaryRestriction, Is.EqualTo("arien-06-打断施法"));
            string before=game.ExportSave();
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.BeginPrimary)).Code, Is.EqualTo("primary_restricted")); Assert.That(game.ExportSave(), Is.EqualTo(before));
            Assert.That(game.View(1).CanPass, Is.True); Apply(game,1,CommandKind.Pass);
            Assert.That(game.View(null).Effects.Count, Is.EqualTo(1));
        }
        [Test]
        public void SilenceDoesNotPreventAnAttackOrTheDefenderResponse()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog,silence:true,againstAttack:true); Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(1).CanBeginPrimary, Is.True); Assert.That(game.View(1).PrimaryRestriction, Is.Empty);
            Apply(game,1,CommandKind.BeginPrimary); Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");
            Assert.That(game.View(0).DefenseOptions.Any(o => o.CardId=="arien-13-挑战者"), Is.True);
            Apply(game,0,CommandKind.Defend,"arien-13-挑战者"); Assert.That(game.View(null).Units.Any(u => u.Seat==0), Is.True);
            Assert.That(game.View(null).Effects.Single().Kind, Is.EqualTo(EffectKind.SkillSuppression));
        }
        [Test]
        public void LegacyRulesKeepTheSkillUnsupportedUntilAnExplicitVersionChange()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog,engineVersion:1);
            Assert.That(game.View(0).PrimarySupported, Is.False);
            string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.BeginPrimary)).Code, Is.EqualTo("primary_not_implemented")); Assert.That(game.ExportSave(), Is.EqualTo(before));
            Assert.That(LocalGameFactory.Restore(catalog,before).ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void PassingOrUsingTheSkillCardAsSecondaryDefenseNeverActivatesItsAura()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog); Apply(game,0,CommandKind.Pass);
            Assert.That(game.View(null).Effects, Is.Empty);
            game=CombatFlowTests.Duel(catalog);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Apply(game,1,CommandKind.Defend,"wasp-06-静电封锁"); Assert.That(game.View(null).Effects, Is.Empty);
        }
    }
}

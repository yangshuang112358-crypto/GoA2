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
    public sealed class UndyingLoyaltyTests
    {
        internal const string Card="shargatha-17-至死不渝", Red="shargatha-01-劈砍";
        internal static GameSession Ready(ContentCatalog cat, bool neighbor=true, bool discarded=true, string enemy="wasp-01-电击", bool secondary=false, string friend="brogan-06-铜墙铁壁")
        {
            var game=LocalGameFactory.Create(cat,"undying-loyalty",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"shargatha,wasp,brogan,arien");Apply(game,0,CommandKind.DebugEquipCard,Card,target:0);
            Apply(game,0,CommandKind.DebugEquipCard,enemy,target:1);
            if(friend!="brogan-06-铜墙铁壁")Apply(game,0,CommandKind.DebugEquipCard,friend,target:2);
            for(int i=0;i<4;i++)Apply(game,0,CommandKind.DebugTeleport,"hero:"+i,cell:new[]{new Hex(6,-8),new Hex(7,-8),new Hex(7,-9),new Hex(6,-6)}[i]);
            if(neighbor)Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(6,-9));
            if(discarded)Apply(game,0,CommandKind.DebugDiscard,Red,target:0);
            string[] cards={secondary?"shargatha-00-反击":Card,enemy,friend,"arien-07-潮水"};
            for(int i=0;i<4;i++)Apply(game,i,CommandKind.SelectCard,cards[i]);
            OpportuneMomentTests.AdvanceTo(game,0);return game;
        }
        private static void Cast(GameSession game,bool skip=false)
        {Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseRecoveredCard,skip?"skip":Red);}
        [Test] public void ExactTextAndVersionGate()
        {
            var card=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);
            Assert.That(CombatRules.HasPrimaryProgram(card,63),Is.False);card.Text+="无条件";Assert.That(CombatRules.HasPrimaryProgram(card),Is.False);
        }
        [TestCase(false)] [TestCase(true)] public void ImmunityRequiresActualRecoveryAndBeginsOnlyAfterChoice(bool skip)
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);game=ChargeTests.Restore(cat,game);
            Apply(game,0,CommandKind.ChooseRecoveredCard,skip?"skip":Red);
            Assert.That(game.View(0).Effects.Count(e=>e.SourceCardId==Card),Is.EqualTo(skip?0:1));
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Red).Zone,Is.EqualTo(skip?CardZone.Discarded:CardZone.InHand));
            ChargeTests.Restore(cat,game);
        }
        [TestCase(false,true)] [TestCase(true,false)] public void NoNeighborOrNoDiscardDoesNotGrantImmunity(bool neighbor,bool discard)
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat,neighbor,discard);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);Assert.That(game.View(0).Pending?.Kind,Is.Not.EqualTo("recover_discard"));ChargeTests.Restore(cat,game);
        }
        [Test] public void ImmuneHeroCannotBeMainAttackTargetOrTheExtraDiscardTarget()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Cast(game);OpportuneMomentTests.AdvanceTo(game,1);
            Assert.That(game.View(1).AttackTargets,Does.Not.Contain("hero:0"));Assert.That(game.View(1).AttackTargets,Does.Contain("hero:2"));
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:2");
            Assert.That(game.View(1).Pending.Kind,Is.EqualTo("defense"));Assert.That(game.View(0).ForcedDiscardCards,Is.Empty);ChargeTests.Restore(cat,game);
        }
        [Test] public void AttackImmunityDoesNotBlockDisplacementFromASkill()
        {
            const string boost="wasp-14-动力助推";var cat=BattlefieldTests.Catalog();var game=Ready(cat,enemy:boost);Cast(game);OpportuneMomentTests.AdvanceTo(game,1);
            Apply(game,1,CommandKind.BeginPrimary);Assert.That(game.View(1).EffectTargets,Does.Contain("hero:0"));
            Apply(game,1,CommandKind.ChooseEffectTarget,"hero:0");
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(4,-8)));ChargeTests.Restore(cat,game);
        }
        [TestCase("turn")] [TestCase("defeat")]
        public void SourceLifecycleCancelsAttackImmunity(string reason)
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Cast(game);
            if(reason=="turn")Apply(game,0,CommandKind.DebugAdvance,"turn");
            else if(reason=="defeat")Apply(game,0,CommandKind.DebugDefeatHero,"hero:0",target:1);
            Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,game);
        }
        [Test] public void BardReturningResolvedSourceImmediatelyCancelsImmunity()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat,friend:"brogan-10-吟游诗人");Cast(game);OpportuneMomentTests.AdvanceTo(game,2);
            Apply(game,2,CommandKind.BeginPrimary);Apply(game,2,CommandKind.ChooseEffectTarget,"hero:0");game=ChargeTests.Restore(cat,game);
            Apply(game,0,CommandKind.ChooseRecoveredCard,Card);
            Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Card).Zone,Is.EqualTo(CardZone.InHand));ChargeTests.Restore(cat,game);
        }
        [TestCase(2)] [TestCase(3)] public void FlashingBladeAttachedMoveCannotSelectAnImmuneFriendOrEnemy(int movedSeat)
        {
            var cat=BattlefieldTests.Catalog();var immune=Ready(cat);Cast(immune);
            var effect=new JsonStateCodec().Read(immune.ExportSave()).Effects.Single(e=>e.SourceCardId==Card);
            var game=FlashingBladeTests.Setup(cat);FlashingBladeTests.Target(game);
            // Isolated query fixture: a resolved immunity is assigned to each allegiance in turn.
            var state=new JsonStateCodec().Read(game.ExportSave());Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Contain("hero:"+movedSeat));
            effect.ControllerSeat=movedSeat;effect.SourceUnitId="hero:"+movedSeat;state.Effects.Add(effect);
            Assert.That(GameRules.LegalEffectTargets(cat,state,0),Does.Not.Contain("hero:"+movedSeat));
        }
        [Test] public void InvalidRecoveryCannotCreateImmunityAndRetryCannotDuplicateIt()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.BeginPrimary);
            foreach(var cmd in new[]{Cmd(game,1,CommandKind.ChooseRecoveredCard,Red),Cmd(game,0,CommandKind.ChooseRecoveredCard,Card)})
            {string before=game.ExportSave();Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            foreach(int? viewer in new int?[]{null,1,2,3})Assert.That(game.View(viewer).RecoverableCards,Is.Empty);
            var pay=Cmd(game,0,CommandKind.ChooseRecoveredCard,Red);Assert.That(game.Execute(0,pay).Accepted,Is.True);
            string after=game.ExportSave();Assert.That(game.Execute(0,pay).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.View(0).Effects.Count(e=>e.SourceCardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,game);
        }
        [Test] public void SecondaryDefenseDoesNotRecoverOrGrantImmunity()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat,secondary:true);Apply(game,0,CommandKind.Pass);OpportuneMomentTests.AdvanceTo(game,1);
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");
            if(game.View(1).Pending.Kind=="effect_target")Apply(game,1,CommandKind.ChooseEffectTarget,"skip");
            Apply(game,0,CommandKind.Defend,Card);Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Red).Zone,Is.EqualTo(CardZone.Discarded));ChargeTests.Restore(cat,game);
        }
        [Test] public void Frozen63CounterWindowKeepsExactBytes()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine63-counterattack-nested.json"));
            var game=LocalGameFactory.Restore(cat,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
        }
        [Test] public void AttackCancellationCannotCancelImmunityAndAttackSilenceCannotSuppressIt()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Cast(game);
            // Query fixture built from the actual resolved skill; no command-replay claim for injected auras.
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(EffectRules.CancellableAdjacentSkills(cat,state,1),Is.Empty);
            state.Effects.Add(new ActiveEffect{Id="query-silence",Kind=EffectKind.SkillSuppression,SourceCardId="wasp-00-闪耀之刃",SourceUnitId="hero:1",ControllerSeat=1,AreaKind=EffectAreaKind.Adjacent,Window=new EffectWindow{StartRound=state.Round,StartTurn=state.Turn,EndRound=state.Round,EndTurn=state.Turn}});
            Assert.That(EffectRules.SkillRestriction(cat,state,0,cat.Card(Card)),Is.Empty);
            state.Effects.Last().SourceCardId="arien-06-打断施法";
            Assert.That(EffectRules.SkillRestriction(cat,state,0,cat.Card(Card)),Is.EqualTo("arien-06-打断施法"));
        }
        [Test] public void FriendlyAndEnemyAttackSourcesRespectImmunityButSelfActionsRemainPossible()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Cast(game);var state=new JsonStateCodec().Read(game.ExportSave());
            var target=state.Units.Single(u=>u.Seat==0);
            foreach(int seat in new[]{1,2,3})Assert.That(EffectRules.CanBeAttacked(state,state.Units.Single(u=>u.Seat==seat),target,false),Is.False);
            Assert.That(EffectRules.CanBeAttacked(state,target,target,false),Is.True);
        }
    }
}

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
    public sealed class CoveringFireTests
    {
        internal const string Card="sabina-15-火力掩护";
        internal static GameSession Ready(ContentCatalog cat,bool neighbor=true)
        {
            var game=DrillTests.Setup(cat,card:Card);OpportuneMomentTests.AdvanceTo(game,0);
            Apply(game,0,CommandKind.DebugTeleport,DrillTests.Melee,cell:new Hex(neighbor?4:1,-8));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(5,-8));
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(4,-9));return game;
        }
        private static void Choose(GameSession game)
        {Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectTarget,"hero:1");}
        [Test] public void ExactBindingAndOldEngineGate()
        {
            var c=BattlefieldTests.Catalog().Card(Card);Assert.That(c.SubtypeValue,Is.EqualTo(3));Assert.That(c.Initiative,Is.EqualTo(10));
            Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,69),Is.False);
            c.Text+="如果可行";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
        }
        [Test] public void FriendlyMinionConditionAndEnemyHeroCandidates()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectTargets,Is.EquivalentTo(new[]{"hero:1","hero:3"}));ChargeTests.Restore(cat,game);
            game=Ready(cat,false);Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(4,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Kind,Is.Not.EqualTo("effect_target"));Assert.That(game.View(0).Events.Any(e=>e.Kind=="ForcedDiscardRequired"),Is.False);ChargeTests.Restore(cat,game);
        }
        [Test] public void ProtectedHeavyStillMeetsTheCondition()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat,false);Apply(game,0,CommandKind.DebugTeleport,DrillTests.Heavy,cell:new Hex(4,-8));
            Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).EffectTargets,Does.Contain("hero:1"));ChargeTests.Restore(cat,game);
        }
        [Test] public void PaymentIsPrivateAtomicAndDuplicateDoesNotDiscardAgain()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Choose(game);game=ChargeTests.Restore(cat,game);
            Assert.That(game.View(1).CanDeclineRetaliationDiscard,Is.True);string card=game.View(1).ForcedDiscardCards.First(),before=game.ExportSave();
            Assert.That(game.View(0).ForcedDiscardCards,Is.Empty);Assert.That(game.View(null).ForcedDiscardCards,Is.Empty);
            foreach(var cmd in new[]{Cmd(game,0,CommandKind.ForcedDiscard,card),Cmd(game,0,CommandKind.DeclineRetaliationDiscard),Cmd(game,1,CommandKind.ForcedDiscard,"skip"),Cmd(game,1,CommandKind.ForcedDiscard,"tigerclaw-07-伺机待发")})
            {Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var pay=Cmd(game,1,CommandKind.ForcedDiscard,card);Assert.That(game.Execute(1,pay).Accepted,Is.True);before=game.ExportSave();
            Assert.That(game.Execute(1,pay).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,game);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackCalculated" || e.Kind=="HeroDefeated"),Is.False);
        }
        [TestCase(false,false)] [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)]
        public void RefusalAndEmptyHandUseNormalDefeatRewardsAndVictory(bool empty,bool victory)
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);if(victory)Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);
            if(empty)foreach(var c in game.View(1).OwnCards.Where(c=>c.Zone==CardZone.InHand).ToArray())Apply(game,0,CommandKind.DebugDiscard,c.CardId,target:1);
            Choose(game);if(!empty)Apply(game,1,CommandKind.DeclineRetaliationDiscard);
            Assert.That(game.View(0).Units.Any(u=>u.Seat==1),Is.False);Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(1));Assert.That(game.View(0).Players[2].Gold,Is.EqualTo(1));
            Assert.That(game.View(0).Events.Last(e=>e.Kind=="HeroDefeated").CardId,Is.EqualTo(Card));Assert.That(game.View(0).Phase==Phase.Finished,Is.EqualTo(victory));ChargeTests.Restore(cat,game);
        }
        [Test] public void SkillRadiusIgnoresRangedBonusAndAttackOnlyImmunity()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(game.ExportSave());
            s.Units.Single(u=>u.Seat==3).Position=new Hex(7,-8);s.Players[0].RangedBonus=8;
            s.Effects.Add(new ActiveEffect{SourceCardId="shargatha-17-至死不渝",SourceUnitId="hero:1",ControllerSeat=1,Kind=EffectKind.AttackActionImmunity,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:1"}));s.Players[0].RangeBonus=1;
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EquivalentTo(new[]{"hero:1","hero:3"}));s.Effects[0].Kind=EffectKind.ImmunityAndUnitTraversal;
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:3"}));
        }
        [Test] public void ActualForcedDiscardTriggersCounterattackBeforeParentFinishesOnce()
        {
            var cat=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(cat,"covering-counter",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"shargatha,sabina,brogan,wasp");Apply(game,0,CommandKind.DebugEquipCard,Card,target:1);
            var pos=new[]{new Hex(6,-8),new Hex(7,-8),new Hex(4,-8),new Hex(6,-9)};
            for(int i=0;i<4;i++)Apply(game,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);
            Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(5,-8));Apply(game,0,CommandKind.DebugTeleport,"minion:1,-1",cell:new Hex(7,-7));
            string[] cards={CounterattackTests.Card,Card,"brogan-06-铜墙铁壁","wasp-07-抵挡屏障"};for(int i=0;i<4;i++)Apply(game,i,CommandKind.SelectCard,cards[i]);
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseEffectTarget,"hero:0");Apply(game,0,CommandKind.ForcedDiscard,CounterattackTests.Slash);
            Assert.That(game.View(0).Pending.Kind,Is.EqualTo("discard_attack"));game=ChargeTests.Restore(cat,game);
            Apply(game,0,CommandKind.ChooseDiscardAttack,CounterattackTests.Slash);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:3");Apply(game,3,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,game);
        }
        [Test] public void Old69SaveRetainsBytesAndExcludesThisCard()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine69-magic-water-placement.json"));
            var game=LocalGameFactory.Restore(cat,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
        }
    }
}

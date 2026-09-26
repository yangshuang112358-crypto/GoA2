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
    public sealed class EnergyExplosionTests
    {
        internal const string Card="wasp-05-电能爆炸";
        internal static GameSession Ready(ContentCatalog cat,bool counter=false)
        {
            var g=LocalGameFactory.Create(cat,"energy-explosion",new[]{"A","B","C","D"},42,true);
            Apply(g,0,CommandKind.DebugPrepare,"wasp,sabina,brogan,shargatha");Apply(g,0,CommandKind.DebugEquipCard,Card,target:0);
            var pos=new[]{new Hex(6,-8),new Hex(7,-8),new Hex(5,-8),counter?new Hex(6,-9):new Hex(6,-6)};
            for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);
            if(counter)Apply(g,0,CommandKind.DebugTeleport,"minion:1,0",cell:new Hex(5,-9));
            string[] cards={Card,"sabina-07-指挥","brogan-06-铜墙铁壁",CounterattackTests.Card};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);
            if(counter){Apply(g,3,CommandKind.BeginPrimary);Apply(g,3,CommandKind.ChooseAttackTarget,"minion:1,0");}
            OpportuneMomentTests.AdvanceTo(g,0);return g;
        }
        internal static void MainTarget(GameSession g,string target="hero:1")
        {Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,target);}
        [Test] public void ExactBindingGateAndMeleeDespiteRadiusIcon()
        {
            var cat=BattlefieldTests.Catalog();var c=cat.Card(Card);Assert.That(c.PrimaryValue,Is.EqualTo(6));Assert.That(c.SubtypeValue,Is.EqualTo(2));
            Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,70),Is.False);c.Text+="如果可能";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
            cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).AttackTargets,Is.EqualTo(new[]{"hero:1"}));
        }
        [Test] public void OptionalTargetExcludesOriginalAndSkipContinuesOriginalAttack()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);MainTarget(g);Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));
            Assert.That(g.View(0).Pending.Optional,Is.True);g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectTarget,"skip");
            Assert.That(g.View(1).Pending.Kind,Is.EqualTo("defense"));Assert.That(g.View(1).Pending.UnitId,Is.EqualTo("hero:1"));
            Assert.That(g.View(0).Events.Any(e=>e.Kind=="ForcedDiscardRequired"),Is.False);Apply(g,1,CommandKind.DeclineDefense);ChargeTests.Restore(cat,g);
        }
        [Test] public void PaymentPausesBeforeAttackAndDoesNotOverwriteOriginalTarget()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);MainTarget(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");
            Assert.That(g.View(3).CanDeclineRetaliationDiscard,Is.True);Assert.That(g.View(0).Events.Any(e=>e.Kind=="AttackCalculated"),Is.False);
            Assert.That(g.View(0).ForcedDiscardCards,Is.Empty);g=ChargeTests.Restore(cat,g);
            Apply(g,3,CommandKind.ForcedDiscard,CounterattackTests.Slash);Assert.That(g.View(1).Pending.UnitId,Is.EqualTo("hero:1"));
            Apply(g,1,CommandKind.DeclineDefense);ChargeTests.Restore(cat,g);
        }
        [TestCase(false,false)] [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)]
        public void ExtraHeroRefusalOrEmptyHandDefeatsAndVictoryStopsOriginalAttack(bool empty,bool victory)
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);if(victory)Apply(g,0,CommandKind.DebugSetCrystal,"1",target:3);
            if(empty)foreach(var c in g.View(3).OwnCards.Where(c=>c.Zone==CardZone.InHand).ToArray())Apply(g,0,CommandKind.DebugDiscard,c.CardId,target:3);
            MainTarget(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");if(!empty)Apply(g,3,CommandKind.DeclineRetaliationDiscard);
            Assert.That(g.View(0).Units.Any(u=>u.Seat==3),Is.False);Assert.That(g.View(0).Players[0].Gold,Is.EqualTo(1));Assert.That(g.View(0).Players[2].Gold,Is.EqualTo(1));
            Assert.That(g.View(0).Phase==Phase.Finished,Is.EqualTo(victory));
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackCalculated"),Is.EqualTo(victory?0:1));
            if(!victory){Assert.That(g.View(1).Pending.UnitId,Is.EqualTo("hero:1"));Apply(g,1,CommandKind.DeclineDefense);}ChargeTests.Restore(cat,g);
        }
        [Test] public void InvalidChoicesAreAtomicAndPaymentRetryIsIdempotent()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);MainTarget(g);string before=g.ExportSave();
            foreach(var cmd in new[]{Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:3"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:2")})
            {Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");before=g.ExportSave();
            Assert.That(g.Execute(0,Cmd(g,0,CommandKind.DeclineRetaliationDiscard)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
            var pay=Cmd(g,3,CommandKind.ForcedDiscard,CounterattackTests.Slash);Assert.That(g.Execute(3,pay).Accepted,Is.True);before=g.ExportSave();
            Assert.That(g.Execute(3,pay).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);
        }
        [Test] public void AttackImmunityExcludesExtraHeroAndOnlySkillRadiusExtendsChoice()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);MainTarget(g);var s=new JsonStateCodec().Read(g.ExportSave());
            s.Units.Single(u=>u.Seat==3).Position=new Hex(6,-5);s.Players[0].RangedBonus=8;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);
            s.Players[0].RangeBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:3"}));
            s.Effects.Add(new ActiveEffect{SourceUnitId="hero:3",ControllerSeat=3,SourceCardId="shargatha-17-至死不渝",Kind=EffectKind.AttackActionImmunity,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);
        }
        [Test] public void MinionPrimaryTargetResolvesAfterExtraHeroPayment()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(6,-7));
            MainTarget(g,"minion:-1,-3");Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,CounterattackTests.Slash);
            Assert.That(g.View(0).Units.Any(u=>u.Id=="minion:-1,-3"),Is.False);Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
        }
        [Test] public void CounterattackWaitsUntilOriginalAttackEndsAndResumesParentOnce()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat,true);MainTarget(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,CounterattackTests.Slash);
            Assert.That(g.View(1).Pending.Kind,Is.EqualTo("defense"));Assert.That(g.View(1).Pending.UnitId,Is.EqualTo("hero:1"));g=ChargeTests.Restore(cat,g);
            Apply(g,1,CommandKind.DeclineDefense);Assert.That(g.View(3).Pending.Kind,Is.EqualTo("discard_attack"));g=ChargeTests.Restore(cat,g);
            Apply(g,3,CommandKind.ChooseDiscardAttack,CounterattackTests.Slash);Apply(g,3,CommandKind.ChooseAttackTarget,"hero:0");Apply(g,0,CommandKind.DeclineDefense);
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
        }
        [Test] public void NoExtraHeroStillExecutesTheChosenAttack()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-5));MainTarget(g);
            Assert.That(g.View(1).Pending.Kind,Is.EqualTo("defense"));Assert.That(g.View(1).Pending.UnitId,Is.EqualTo("hero:1"));
            Assert.That(g.View(0).Events.Any(e=>e.Kind=="ForcedDiscardRequired"),Is.False);ChargeTests.Restore(cat,g);
        }
        [Test] public void NoAdjacentAttackTargetCannotUseExtraHeroPaymentAsAnIndependentSkill()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));Apply(g,0,CommandKind.BeginPrimary);
            Assert.That(g.View(0).Events.Any(e=>e.Kind=="ForcedDiscardRequired" || e.Kind=="AttackCalculated"),Is.False);
            Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("effect_target"));ChargeTests.Restore(cat,g);
        }
        [Test] public void Old70PaymentKeepsItsExactBytesAndCapabilities()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine70-covering-fire-payment.json"));
            var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
        }
    }
}

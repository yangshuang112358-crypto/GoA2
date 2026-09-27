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
    public sealed class TorrentTests
    {
        internal const string Card="arien-02-激流";
        internal static GameSession Ready(ContentCatalog cat,bool counter=false,string card=Card,bool defended=false)
        {
            var g=LocalGameFactory.Create(cat,"torrent",new[]{"A","B","C","D"},42,true);
            Apply(g,0,CommandKind.DebugPrepare,"arien,sabina,brogan,shargatha");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);
            if(defended)Apply(g,0,CommandKind.DebugEquipCard,"sabina-08-带头冲锋",target:1);
            var pos=new[]{new Hex(3,-8),new Hex(4,-8),counter?new Hex(5,-7):new Hex(3,-9),new Hex(5,-8)};
            for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);
            if(counter)Apply(g,0,CommandKind.DebugTeleport,"minion:1,0",cell:new Hex(5,-9));
            string[] cards={card,defended?"sabina-00-近身射击":"sabina-07-指挥","brogan-06-铜墙铁壁",CounterattackTests.Card};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);
            if(counter){Apply(g,3,CommandKind.BeginPrimary);Apply(g,3,CommandKind.ChooseAttackTarget,"minion:1,0");}
            OpportuneMomentTests.AdvanceTo(g,0);return g;
        }
        internal static void MainTarget(GameSession g,string target="hero:1")
        {Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseAttackTarget,target);}
        [Test] public void ExactBindingGateAndAdjacentAttack()
        {
            var cat=BattlefieldTests.Catalog();var c=cat.Card(Card);Assert.That(c.PrimaryValue,Is.EqualTo(7));Assert.That(c.SubtypeValue,Is.Null);
            Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,80),Is.False);c.Text+="如果可能";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
            cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).AttackTargets,Is.EqualTo(new[]{"hero:1"}));
        }
        [Test] public void RearTargetIsRequiredWhenAvailableAndCannotSkip()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);MainTarget(g);
            Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));Assert.That(g.View(0).Pending.Optional,Is.False);
            string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);
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
        [Test] public void RearRayHasFixedThreeCellLimitAndAttackImmunityFilter()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);MainTarget(g);var s=new JsonStateCodec().Read(g.ExportSave());
            s.Players[0].RangedBonus=8;s.Players[0].RangeBonus=8;
            foreach(var pos in new[]{new Hex(5,-8),new Hex(7,-8),new Hex(8,-8),new Hex(4,-7),new Hex(2,-8)})
            {s.Units.Single(u=>u.Seat==3).Position=pos;Assert.That(GameRules.LegalEffectTargets(cat,s,0).Contains("hero:3"),Is.EqualTo(pos.Equals(new Hex(5,-8)) || pos.Equals(new Hex(7,-8))));}
            s.Units.Single(u=>u.Seat==3).Position=new Hex(5,-8);
            s.Effects.Add(new ActiveEffect{SourceUnitId="hero:3",ControllerSeat=3,SourceCardId="shargatha-17-至死不渝",Kind=EffectKind.AttackActionImmunity,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);
        }
        [TestCase(1,0)] [TestCase(0,1)] [TestCase(-1,1)] [TestCase(-1,0)] [TestCase(0,-1)] [TestCase(1,-1)]
        public void RearRayFollowsAllSixAttackDirections(int dx,int dy)
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);MainTarget(g);var s=new JsonStateCodec().Read(g.ExportSave());
            s.Units.Single(u=>u.Seat==0).Position=new Hex(0,0);s.Units.Single(u=>u.Seat==1).Position=new Hex(dx,dy);
            s.Units.Single(u=>u.Seat==3).Position=new Hex(dx*4,dy*4);
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:3"}));
            s.Units.Single(u=>u.Seat==3).Position=new Hex(dx*5,dy*5);Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);
        }
        [Test] public void MinionPrimaryTargetResolvesAfterExtraHeroPayment()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(4,-9));Apply(g,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(4,-8));
            MainTarget(g,"minion:-1,-3");Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,CounterattackTests.Slash);
            Assert.That(g.View(0).Units.Any(u=>u.Id=="minion:-1,-3"),Is.False);Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
        }
        [Test] public void CounterattackWaitsUntilOriginalAttackEndsAndResumesParentOnce()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat,true);MainTarget(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,3,CommandKind.ForcedDiscard,CounterattackTests.Slash);
            Assert.That(g.View(1).Pending.Kind,Is.EqualTo("defense"));Assert.That(g.View(1).Pending.UnitId,Is.EqualTo("hero:1"));g=ChargeTests.Restore(cat,g);
            Apply(g,1,CommandKind.DeclineDefense);Assert.That(g.View(3).Pending.Kind,Is.EqualTo("discard_attack"));g=ChargeTests.Restore(cat,g);
            Apply(g,3,CommandKind.ChooseDiscardAttack,CounterattackTests.Slash);Apply(g,3,CommandKind.ChooseAttackTarget,"hero:2");Apply(g,2,CommandKind.DeclineDefense);
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
        }
        [Test] public void NoExtraHeroStillExecutesTheChosenAttack()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(5,-7));MainTarget(g);
            Assert.That(g.View(1).Pending.Kind,Is.EqualTo("defense"));Assert.That(g.View(1).Pending.UnitId,Is.EqualTo("hero:1"));
            Assert.That(g.View(0).Events.Any(e=>e.Kind=="ForcedDiscardRequired"),Is.False);ChargeTests.Restore(cat,g);
        }
        [Test] public void NoAdjacentAttackTargetCannotUseExtraHeroPaymentAsAnIndependentSkill()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(4,-6));Apply(g,0,CommandKind.BeginPrimary);
            Assert.That(g.View(0).Events.Any(e=>e.Kind=="ForcedDiscardRequired" || e.Kind=="AttackCalculated"),Is.False);
            Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("effect_target"));ChargeTests.Restore(cat,g);
        }
        [Test] public void Old80SwapKeepsItsExactBytesAndCapabilities()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine80-shifting-step-choice.json"));
            var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
        }
    }
}

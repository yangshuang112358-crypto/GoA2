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
    public sealed class ShieldBashTests
    {
        internal const string Bash="brogan-15-盾牌猛击";
        internal static GameSession Setup(ContentCatalog catalog,bool noAttack=false,int engine=GameState.CurrentEngineVersion)
        {
            var g=LocalGameFactory.Create(catalog,"bash",new[]{"A","B","C","D"},42,true,engine);Apply(g,0,CommandKind.DebugPrepare,"brogan,sabina,tigerclaw,wasp");Apply(g,0,CommandKind.DebugEquipCard,Bash,target:0);
            foreach(var p in new[]{(0,new Hex(6,-8)),(1,new Hex(7,-8)),(2,new Hex(5,-8)),(3,new Hex(6,-9))})Apply(g,0,CommandKind.DebugTeleport,"hero:"+p.Item1,cell:p.Item2);
            string[] cards={Bash,noAttack?"sabina-07-指挥":"sabina-01-拔枪","tigerclaw-07-伺机待发",noAttack?"wasp-07-抵挡屏障":"wasp-00-闪耀之刃"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);if(!noAttack)Apply(g,3,CommandKind.Pass);Assert.That(g.View(0).ActiveSeat,Is.EqualTo(0));return g;
        }
        [Test]
        public void ExactContractAndVersionGate()
        {var c=BattlefieldTests.Catalog().Card(Bash);Assert.That(c.Initiative,Is.EqualTo(9));Assert.That(c.PrimaryFamily,Is.EqualTo("skill"));Assert.That(c.SecondaryDefense,Is.EqualTo(7));Assert.That(c.SecondaryMovement,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,32),Is.False);c.Text+="否则被击败";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
        [Test]
        public void BothResolvedAndUnresolvedEnemyAttacksQualifyAndMovementUpdatesTargets()
        {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).EffectTargets,Is.EquivalentTo(new[]{"hero:1","hero:3"}));g=Setup(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(4,-9));Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:1"}));ChargeTests.Restore(cat,g);}
        [TestCase(1)] [TestCase(3)]
        public void TargetOwnsPrivateDiscardAndTheSkillDoesNotAttack(int victim)
        {
            var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:"+victim);Assert.That(g.View(victim).CanDeclineRetaliationDiscard,Is.False);Assert.That(g.View(victim).Pending!.Source,Is.EqualTo(Bash));ChargeTests.Restore(cat,g);string card=g.View(victim).ForcedDiscardCards.First();
            foreach(int? viewer in new int?[]{null,0,1,2,3})if(viewer!=victim)Assert.That(g.View(viewer).ForcedDiscardCards,Is.Empty);
            Apply(g,victim,CommandKind.ForcedDiscard,card);Assert.That(g.View(victim).OwnCards.Single(c=>c.CardId==card).Zone,Is.EqualTo(CardZone.Discarded));Assert.That(g.View(null).Events.Any(e=>e.Kind=="AttackCalculated" || e.Kind=="HeroDefeated"),Is.False);Assert.That(g.View(null).Events.Where(e=>e.Kind=="CardDiscarded"),Is.Empty);ChargeTests.Restore(cat,g);
        }
        [Test]
        public void EmptyHandSkipsWithoutDefeat()
        {var cat=BattlefieldTests.Catalog();var g=Setup(cat);foreach(var c in g.View(1).OwnCards.Where(c=>c.Zone==CardZone.InHand).ToArray())Apply(g,0,CommandKind.DebugDiscard,c.CardId,target:1);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.View(1).Pending,Is.Null);Assert.That(g.View(1).Players[1].AwaitingRespawn,Is.False);Assert.That(g.View(null).Events.Any(e=>e.Kind=="ForcedDiscardSkipped"),Is.True);ChargeTests.Restore(cat,g);}
        [Test]
        public void NonAttackEnemiesProduceNoTargetAndNoDiscard()
        {var cat=BattlefieldTests.Catalog();var g=Setup(cat,true);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("forced_discard"));Assert.That(g.View(null).Events.Any(e=>e.Kind=="ForcedDiscardRequired"),Is.False);ChargeTests.Restore(cat,g);}
        [Test]
        public void WrongActorSkipAndRevealedCardAreRejectedAndValidCommandIsIdempotent()
        {
            var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");string before=g.ExportSave();foreach(var c in new[]{Cmd(g,0,CommandKind.ForcedDiscard,"sabina-07-指挥"),Cmd(g,1,CommandKind.ForcedDiscard,"skip"),Cmd(g,1,CommandKind.ForcedDiscard,"sabina-01-拔枪"),Cmd(g,1,CommandKind.DeclineRetaliationDiscard)}){Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            var valid=Cmd(g,1,CommandKind.ForcedDiscard,g.View(1).ForcedDiscardCards.First());Assert.That(g.Execute(1,valid).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(1,valid).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);
        }
        [Test]
        public void FrozenDualSupportWindowStaysUnchanged()
        {var cat=BattlefieldTests.Catalog();string s=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine32-battlearms-defense.json"));var g=LocalGameFactory.Restore(cat,s);Assert.That(g.ExportSave(),Is.EqualTo(s));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Bash));}
    }
}

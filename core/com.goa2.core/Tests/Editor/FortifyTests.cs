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
    public sealed class FortifyTests
    {
        internal const string Card="brogan-11-巩固防线", Heavy="minion:3,1";
        internal static GameSession Ready(ContentCatalog cat,bool solitary=true,bool blockSpawn=false,bool tide=false)
        {
            var g=LocalGameFactory.Create(cat,"fortify",new[]{"A","B","C","D"},42,true);
            Apply(g,0,CommandKind.DebugPrepare,"brogan,wasp,sabina,arien");
            if(tide){Apply(g,0,CommandKind.DebugSetGold,"28",target:3);Apply(g,0,CommandKind.DebugAdvance,"round");Apply(g,0,CommandKind.ResolveRoundEnd);RoundEndTests.FinishUpgrades(g);}
            Apply(g,0,CommandKind.DebugEquipCard,Card,target:0);
            if(tide)Apply(g,0,CommandKind.DebugEquipCard,"arien-07-潮水",target:3);
            if(solitary)foreach(var u in g.View(0).Units.Where(u=>u.Team==Team.Blue && (u.Kind=="melee" || u.Kind=="ranged")).ToList())Apply(g,0,CommandKind.DebugRemoveMinion,u.Id);
            Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-2,0));Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(-2,1));
            Apply(g,0,CommandKind.DebugTeleport,"hero:2",cell:blockSpawn?cat.Cells.First(c=>c.Region=="blueNear" && c.Spawn.Contains("Spawn") && !c.Spawn.Contains("Hero")).Position:new Hex(6,-9));
            Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(7,-9));Apply(g,0,CommandKind.DebugTeleport,Heavy,cell:new Hex(-3,2));
            string[] cards={Card,"wasp-06-静电封锁","sabina-06-并肩作战","arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);
            OpportuneMomentTests.AdvanceTo(g,0);Apply(g,0,CommandKind.BeginPrimary);IronWallTests.FinishTurn(g);return g;
        }
        internal static void Attack(GameSession g)
        {
            string[] cards={"brogan-00-猛攻","wasp-01-电击","sabina-01-拔枪","arien-00-华丽刀锋"};
            for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,1);
            Apply(g,1,CommandKind.BeginPrimary);Apply(g,1,CommandKind.ChooseAttackTarget,Heavy);Apply(g,1,CommandKind.ChooseEffectTarget,"skip");
        }
        [TestCase(true)] [TestCase(false)]
        public void ProtectOrDeclineIsDecidedBeforeHeavyGoldAndFrontline(bool pay)
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Attack(g);var before=g.View(0);
            Assert.That(before.Pending?.Kind,Is.EqualTo("minion_protection"));Assert.That(before.Pending.Source,Is.EqualTo(Card));
            Assert.That(before.RedMarks,Is.Zero);Assert.That(before.Players[1].Gold,Is.Zero);g=ChargeTests.Restore(cat,g);
            Apply(g,0,CommandKind.ChooseMinionProtection,pay?"brogan-13-冲拳":"skip");
            Assert.That(g.View(0).Units.Any(u=>u.Id==Heavy),Is.EqualTo(pay));Assert.That(g.View(1).Players[1].Gold,Is.EqualTo(pay?0:4));
            Assert.That(g.View(0).RedMarks,Is.EqualTo(pay?0:1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="FrontlineAdvanced"),Is.EqualTo(pay?0:1));
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));ChargeTests.Restore(cat,g);
        }
        [Test]
        public void EmptyHandDoesNotOpenChoiceAndHeavyStillPushes()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);foreach(var c in g.View(0).OwnCards.Where(c=>c.Zone==CardZone.InHand).ToList())Apply(g,0,CommandKind.DebugDiscard,c.CardId,target:0);
            Apply(g,1,CommandKind.DebugAttack,Heavy+"|0");Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("minion_protection"));
            Assert.That(g.View(1).Players[1].Gold,Is.EqualTo(4));Assert.That(g.View(0).RedMarks,Is.EqualTo(1));ChargeTests.Restore(cat,g);
        }
        [Test]
        public void ProtectedHeavyCannotBeAttackedOrReceiveThisSkillThroughADebugBypass()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat,solitary:false);var s=new JsonStateCodec().Read(g.ExportSave());
            Assert.That(EffectRules.MinionDefeatProtectors(cat,s,s.Units.Single(u=>u.Id==Heavy)),Is.Empty);
            Assert.That(g.View(1).DebugAttackTargets,Does.Not.Contain(Heavy));string before=g.ExportSave();
            Assert.That(g.Execute(1,Cmd(g,1,CommandKind.DebugAttack,Heavy+"|0")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
            Apply(g,0,CommandKind.DebugDefeatMinion,Heavy,target:1);Assert.That(g.View(0).Events.Any(e=>e.Kind=="MinionProtectionChoiceRequired"),Is.False);
        }
        [Test]
        public void DecliningWaitsForBlockedSpawnAndRestoresTheSameAttackOnce()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat,blockSpawn:true);Attack(g);Apply(g,0,CommandKind.ChooseMinionProtection,"skip");
            Assert.That(g.View(0).Pending?.Kind,Is.EqualTo("minion_spawn"));Assert.That(g.View(1).Players[1].Gold,Is.EqualTo(4));g=ChargeTests.Restore(cat,g);
            for(int i=0;i<12 && g.View(0).Pending?.Kind=="minion_spawn";i++)
            {int seat=g.View(0).Pending.ChooserSeat;var s=new JsonStateCodec().Read(g.ExportSave());Apply(g,seat,CommandKind.ChooseMinionSpawn,cell:GameRules.LegalMinionSpawns(cat,s,seat).First());}
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));Assert.That(g.View(0).Events.Count(e=>e.Kind=="MinionProtectionChoiceRequired"),Is.EqualTo(1));ChargeTests.Restore(cat,g);
        }
        [Test]
        public void TideBattleRemovesHeavyWithoutOfferingDefeatProtection()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat,tide:true);Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(-1,0));
            var cards=new[]{"brogan-00-猛攻","wasp-00-闪耀之刃","sabina-01-拔枪","arien-06-打断施法"};
            for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,3);Apply(g,3,CommandKind.BeginPrimary);Apply(g,3,CommandKind.ChoosePrimaryOption,"battle");
            Apply(g,0,CommandKind.ChooseRoundMinionRemoval,Heavy);Assert.That(g.View(0).Events.Any(e=>e.Kind=="MinionProtectionChoiceRequired"),Is.False);
            Assert.That(g.View(0).RedMarks,Is.EqualTo(1));Assert.That(g.View(0).Events.Any(e=>e.Kind=="GoldAwarded"),Is.False);ChargeTests.Restore(cat,g);
        }
        [TestCase("melee",Team.Blue,true)] [TestCase("ranged",Team.Blue,true)] [TestCase("heavy",Team.Blue,true)] [TestCase("heavy",Team.Red,false)] [TestCase("hero",Team.Blue,false)] [TestCase("marker",Team.Blue,false)]
        public void TaxonomyAndTeamAreLimitedToFriendlyActualMinions(string kind,Team team,bool allowed)
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);var s=new JsonStateCodec().Read(g.ExportSave());var unit=s.Units.Single(u=>u.Id==Heavy);unit.Kind=kind;unit.Team=team;
            Assert.That(EffectRules.MinionDefeatProtectors(cat,s,unit).Any(),Is.EqualTo(allowed));
        }
        [Test]
        public void RangeIsDynamicAndRangedBonusDoesNotIncreaseSkillCoverage()
        {
            var cat=BattlefieldTests.Catalog();var s=new JsonStateCodec().Read(Ready(cat).ExportSave());var unit=s.Units.Single(u=>u.Id==Heavy);
            Assert.That(EffectRules.MinionDefeatProtectors(cat,s,unit).Count,Is.EqualTo(1));unit.Position=new Hex(-5,0);s.Players[0].RangedBonus=8;
            Assert.That(EffectRules.MinionDefeatProtectors(cat,s,unit),Is.Empty);s.Players[0].RangeBonus=1;Assert.That(EffectRules.MinionDefeatProtectors(cat,s,unit).Count,Is.EqualTo(1));
        }
        [TestCase("replace")] [TestCase("defeat")] [TestCase("round")]
        public void SourceCancellationRemovesHeavyProtection(string kind)
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);
            if(kind=="replace")Apply(g,0,CommandKind.DebugEquipCard,ShieldMinionTests.Card,target:0);
            else if(kind=="defeat")Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);
            else Apply(g,0,CommandKind.DebugAdvance,"round");
            Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,g);
        }
        [Test]
        public void PaymentPrivacyInvalidCommandsAndDuplicateCostArePreserved()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Attack(g);string before=g.ExportSave();
            foreach(var c in new[]{Cmd(g,1,CommandKind.ChooseMinionProtection,"skip"),Cmd(g,0,CommandKind.ChooseMinionProtection,Card),Cmd(g,0,CommandKind.DebugRemoveMinion,Heavy)})
            {Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            Assert.That(g.View(1).MinionProtectionCards,Is.Empty);var pay=Cmd(g,0,CommandKind.ChooseMinionProtection,"brogan-13-冲拳");Assert.That(g.Execute(0,pay).Accepted,Is.True);before=g.ExportSave();
            Assert.That(g.Execute(0,pay).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(g.View(null).Events.Any(e=>e.CardId=="brogan-13-冲拳"),Is.False);ChargeTests.Restore(cat,g);
        }
        [Test]
        public void ExactBindingAndOldShieldPaymentRetainHistoricalSemantics()
        {
            var cat=BattlefieldTests.Catalog();var c=cat.Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,60),Is.False);c.Text+="修改";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
            cat=BattlefieldTests.Catalog();string bytes=System.IO.File.ReadAllText(System.IO.Path.Combine(ContentTests.Root(),"tests/fixtures/engine54-shield-payment.json"));
            var g=LocalGameFactory.Restore(cat,bytes);Assert.That(g.ExportSave(),Is.EqualTo(bytes));Apply(g,0,CommandKind.ChooseMinionProtection,"brogan-13-冲拳");ChargeTests.Restore(cat,g);
        }
        [Test]
        public void SameHeavyCanBeSavedTwiceInOneRoundAtSeparateCosts()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);
            foreach(string cost in new[]{"brogan-13-冲拳","brogan-01-冲撞"}){Apply(g,1,CommandKind.DebugAttack,Heavy+"|0");Apply(g,0,CommandKind.ChooseMinionProtection,cost);}
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="MinionDefeatPrevented"),Is.EqualTo(2));Assert.That(g.View(0).RedMarks,Is.Zero);Assert.That(g.View(1).Players[1].Gold,Is.Zero);ChargeTests.Restore(cat,g);
        }
    }
}

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
 public sealed class PoisonDaggerTests
 {
  internal const string Card="tigerclaw-09-淬毒匕首";
  internal static GameSession Ready(ContentCatalog cat,string card=Card)
  {
   var g=LocalGameFactory.Create(cat,"poison",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"tigerclaw,wasp,brogan,shargatha");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);Apply(g,0,CommandKind.DebugEquipCard,"brogan-10-吟游诗人",target:2);
   var pos=new[]{new Hex(3,-8),new Hex(4,-8),new Hex(4,-9),new Hex(3,-9)};for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);var cards=new[]{card,"wasp-07-抵挡屏障","brogan-06-铜墙铁壁","shargatha-06-海妖之歌"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,0);return g;
  }
  internal static GameSession Poison(ContentCatalog cat,int target=1,string card=Card){var g=Ready(cat,card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:"+target);return g;}
  [Test] public void ExactTextAndGate(){var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,90),Is.False);c.Text+="叠加";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(0)] [TestCase(1)] [TestCase(2)] public void TargetsSelfEnemyAndAllyAndReplays(int target)
  {var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).EffectTargets,Does.Contain("hero:"+target));Assert.That(g.View(0).EffectTargets.All(t=>t.StartsWith("hero:")),Is.True);g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:"+target);Assert.That(g.ExportSave(),Does.Contain("PoisonMarkers"));ChargeTests.Restore(cat,g);}
  [TestCase(0)] [TestCase(1)] [TestCase(3)] public void ReversesEachAttackPassiveNotPrintedOrExtraBonuses(int count)
  {var cat=BattlefieldTests.Catalog();var g=Poison(cat);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[1].AttackBonus=count;var attack=cat.Card("wasp-00-闪耀之刃");var a=CombatMath.Attack(s,attack,1,"hero:0",extraAttack:2,ultimateAttack:3);Assert.That(a.BaseAttack,Is.EqualTo(3));Assert.That(a.AttackBonus,Is.EqualTo(5-count));Assert.That(s.Players[1].AttackBonus,Is.EqualTo(count));}
  [Test] public void ImmunityAndRangeFilterAreAuthoritativeAndInvalidChoiceIsAtomic()
  {var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).EffectTargets,Does.Not.Contain("hero:1"));string before=g.ExportSave();Assert.That(g.Execute(1,Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:0")).Accepted,Is.False);Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));var s=new JsonStateCodec().Read(before);s.Units.Single(u=>u.Seat==1).Position=new Hex(7,-8);s.Players[0].RangedBonus=1;s.Effects.Add(new ActiveEffect{SourceUnitId="hero:1",Kind=EffectKind.ImmunityAndUnitTraversal,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));s.Effects.Clear();Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Contain("hero:1"));}
  [Test] public void SourceAndHolderDefeatDoNotRemovePoison()
  {var cat=BattlefieldTests.Catalog();var g=Poison(cat);Apply(g,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Apply(g,0,CommandKind.DebugDefeatHero,"hero:1",target:2);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[1].AttackBonus=2;s.Units.Add(new UnitState{Id="hero:1",Seat=1,Kind="hero",Team=Team.Red,Position=new Hex(5,-8)});Assert.That(CombatMath.Attack(s,cat.Card("wasp-00-闪耀之刃"),1,"hero:2").AttackBonus,Is.EqualTo(-2));ChargeTests.Restore(cat,g);}
  [Test] public void RetrievalAndLegalSameRoundReapplicationDoNotStack()
  {var cat=BattlefieldTests.Catalog();var g=Poison(cat);Apply(g,0,CommandKind.DebugAdvance,"turn");var cards=new[]{"tigerclaw-00-瞬闪打击","wasp-00-闪耀之刃","brogan-10-吟游诗人","shargatha-13-石化"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,2);Apply(g,2,CommandKind.BeginPrimary);Apply(g,2,CommandKind.ChooseEffectTarget,"hero:0");Apply(g,0,CommandKind.ChooseRecoveredCard,Card);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[1].AttackBonus=2;Assert.That(CombatMath.Attack(s,cat.Card("wasp-00-闪耀之刃"),1,"hero:0").AttackBonus,Is.EqualTo(-2));
   cards=new[]{Card,"wasp-01-电击","brogan-01-冲撞","shargatha-01-劈砍"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,0);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");s=new JsonStateCodec().Read(g.ExportSave());s.Players[1].AttackBonus=2;Assert.That(CombatMath.Attack(s,cat.Card("wasp-00-闪耀之刃"),1,"hero:0").AttackBonus,Is.EqualTo(-2));ChargeTests.Restore(cat,g);
  }
  [Test] public void RoundEndClearsPoison()
  {var cat=BattlefieldTests.Catalog();var g=Poison(cat);Apply(g,0,CommandKind.DebugAdvance,"round");Apply(g,0,CommandKind.ResolveRoundEnd);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[1].AttackBonus=2;Assert.That(CombatMath.Attack(s,cat.Card("wasp-00-闪耀之刃"),1,"hero:0").AttackBonus,Is.EqualTo(2));ChargeTests.Restore(cat,g);}
  [Test] public void RecomputesNextUnactedInitiativeWithoutChangingRawUpgradeCounts()
  {var cat=BattlefieldTests.Catalog();var g=Poison(cat);Apply(g,0,CommandKind.DebugAdvance,"turn");var s=new JsonStateCodec().Read(g.ExportSave());s.Players[1].InitiativeBonus=2;s.Players[2].InitiativeBonus=1;var cards=new[]{"tigerclaw-02-偷袭","wasp-00-闪耀之刃","brogan-00-猛攻","shargatha-00-反击"};for(int i=0;i<4;i++)new GameRules().Apply(cat,s,new Command{ActorSeat=i,Kind=CommandKind.SelectCard,Value=cards[i]});Assert.That(s.ActiveSeat,Is.EqualTo(2));Assert.That(s.Players[1].InitiativeBonus,Is.EqualTo(2));}
  [Test] public void LowPoisonLeavesDefenseAndOtherPassivesIntactAndProjectsEffectiveValues()
  {var cat=BattlefieldTests.Catalog();var g=Poison(cat);Apply(g,0,CommandKind.DebugAdvance,"turn");var s=new JsonStateCodec().Read(g.ExportSave());s.Players[1].AttackBonus=2;s.Players[1].InitiativeBonus=1;s.Players[1].DefenseBonus=3;s.Players[1].MovementBonus=4;var projected=new GameSession(cat,new JsonStateCodec(),s).View(0).Players[1];Assert.That(projected.IsPoisoned,Is.True);Assert.That(projected.PermanentBonuses["攻击"],Is.EqualTo(2));Assert.That(projected.EffectiveBonuses["攻击"],Is.EqualTo(-2));Assert.That(projected.EffectiveBonuses["先攻"],Is.EqualTo(-1));Assert.That(projected.EffectiveBonuses["防御"],Is.EqualTo(3));Assert.That(projected.EffectiveBonuses["移动"],Is.EqualTo(4));new GameRules().Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.DebugAttack,Value="hero:1|5"});Assert.That(CombatRules.DefenseOptions(cat,s,1),Is.Not.Empty);Assert.That(CombatRules.DefenseOptions(cat,s,1).All(o=>o.Assessment.DefenseBonus==3),Is.True);}
  [Test] public void MarkerDoesNotDuplicateOnRetriedCommand()
  {var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var cmd=Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1");Assert.That(g.Execute(0,cmd).Accepted,Is.True);var before=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(new JsonStateCodec().Read(before).PoisonMarkers.Count,Is.EqualTo(1));}
  [Test] public void Old90PendingBytesUnchanged(){var cat=BattlefieldTests.Catalog();var save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine90-flood-internal.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));}
 }
}

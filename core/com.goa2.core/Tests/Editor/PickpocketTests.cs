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
 public sealed class PickpocketTests
 {
  internal const string Pick="tigerclaw-08-偷天妙手";
  internal static GameSession Setup(ContentCatalog cat,bool boundary=false,bool suppression=false)
  {
   var g=LocalGameFactory.Create(cat,"pick",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,suppression ? "tigerclaw,arien,brogan,sabina" : "tigerclaw,wasp,brogan,sabina");Apply(g,0,CommandKind.DebugEquipCard,Pick,target:0);
   foreach(var p in new[]{(0,new Hex(6,-8)),(1,new Hex(7,-8)),(2,new Hex(5,-8)),(3,new Hex(6,-9))})Apply(g,0,CommandKind.DebugTeleport,"hero:"+p.Item1,cell:p.Item2);
   Apply(g,0,CommandKind.DebugSetGold,"3",target:1);
   string[] cards={Pick,suppression ? "arien-06-打断施法" : "wasp-06-静电封锁","brogan-06-铜墙铁壁","sabina-07-指挥"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);
   while(g.View(0).ActiveSeat!=0){var v=g.View(0);if(v.Phase==Phase.InitiativeChoice)Apply(g,v.Pending!.ChooserSeat,CommandKind.ChooseInitiative,target:0);else Apply(g,v.ActiveSeat!.Value,(boundary || suppression) && v.ActiveSeat==1 ? CommandKind.BeginPrimary : CommandKind.Pass);}return g;
  }
  internal static void GoldStep(GameSession g){Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectMove,"skip");}
  [Test] public void ContractAndVersionGate()
  {var c=BattlefieldTests.Catalog().Card(Pick);Assert.That(c.PrimaryFamily,Is.EqualTo("skill"));Assert.That(c.Initiative,Is.EqualTo(2));Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,36),Is.False);c.Text+="可攻击";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
  [TestCase(0)] [TestCase(1)] public void OptionalGoldTransferConservesCoinsAndRequiresFinalMove(int amount)
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);int total=g.View(0).Players.Sum(p=>p.Gold);GoldStep(g);Assert.That(g.View(0).Pending!.Kind,Is.EqualTo("gold_transfer"));Assert.That(g.View(0).Pending!.CandidateSeats,Is.EqualTo(new[]{1}));g=ChargeTests.Restore(cat,g);
   Apply(g,0,CommandKind.ChooseGoldTransfer,amount.ToString(),target:amount==0?-1:1);Assert.That(g.View(0).Players.Sum(p=>p.Gold),Is.EqualTo(total));Assert.That(g.View(0).Players[1].Gold,Is.EqualTo(3-amount));Assert.That(g.View(0).Players[0].Gold,Is.EqualTo(amount));
   Assert.That(g.View(0).Pending!.ResumeAt,Is.EqualTo("required_straight_if_able"));Assert.That(g.View(0).Pending!.Optional,Is.False);Assert.That(g.View(0).EffectMoves.All(m=>m.Path.Count==3),Is.True);g=ChargeTests.Restore(cat,g);
   string before=g.ExportSave();Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectMove,"skip")).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectMove,cell:g.View(0).EffectMoves.First().Destination);Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Pick),Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void InvalidAmountsTargetsActorsAndDuplicatesCannotCreateMoney()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);GoldStep(g);string before=g.ExportSave();
   foreach(var c in new[]{Cmd(g,0,CommandKind.ChooseGoldTransfer,"2",target:1),Cmd(g,0,CommandKind.ChooseGoldTransfer,"-1",target:1),Cmd(g,0,CommandKind.ChooseGoldTransfer,"1",target:2),Cmd(g,0,CommandKind.ChooseGoldTransfer,"1",target:3),Cmd(g,1,CommandKind.ChooseGoldTransfer,"1",target:1),Cmd(g,0,CommandKind.ChooseGoldTransfer,"0",target:1)}){Assert.That(g.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
   var valid=Cmd(g,0,CommandKind.ChooseGoldTransfer,"1",target:1);Assert.That(g.Execute(0,valid).Accepted,Is.True);before=g.ExportSave();Assert.That(g.Execute(0,valid).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(g.View(0).Players[0].Gold,Is.EqualTo(1));ChargeTests.Restore(cat,g);
  }
  [Test] public void EmptyEnemyPurseSkipsGoldButStillMoves()
  {var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.DebugSetGold,"0",target:1);GoldStep(g);Assert.That(g.View(0).Pending!.ResumeAt,Is.EqualTo("required_straight_if_able"));Assert.That(g.View(0).Players.Sum(p=>p.Gold),Is.Zero);ChargeTests.Restore(cat,g);}
  [Test] public void MovementBudgetDoesNotAddPassiveAndMoneyTargetsUseNewPosition()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);Apply(g,0,CommandKind.BeginPrimary);var state=new JsonStateCodec().Read(g.ExportSave());state.Players[0].MovementBonus=20;Assert.That(GameRules.LegalEffectMoves(cat,state,0).All(m=>m.Path.Count<=3),Is.True);
   var option=g.View(0).EffectMoves.First(m=>m.Destination.Distance(new Hex(7,-8))>1);g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectMove,cell:option.Destination);Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("gold_transfer"));Assert.That(g.View(0).Players[1].Gold,Is.EqualTo(3));ChargeTests.Restore(cat,g);
  }
  [Test] public void NoExitSkipsBothMovementStepsWithoutLosingOrCreatingGold()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat);var free=new Hex(6,-8).Neighbors().Where(h=>cat.Cell(h)?.Obstacle==false && !g.View(0).Units.Any(u=>u.Position==h)).ToArray();var minions=g.View(0).Units.Where(u=>u.Kind!="hero" && u.Position.Distance(new Hex(6,-8))>1).ToArray();for(int i=0;i<free.Length;i++)Apply(g,0,CommandKind.DebugTeleport,minions[i].Id,cell:free[i]);
   Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).Pending!.Kind,Is.EqualTo("gold_transfer"));Apply(g,0,CommandKind.ChooseGoldTransfer,"0");Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Pick),Is.EqualTo(1));Assert.That(g.View(0).Players[1].Gold,Is.EqualTo(3));ChargeTests.Restore(cat,g);
  }
  [Test] public void StaticLockConstrainsBothTextMovesAndGoldChoiceStaysWithSource()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat,boundary:true);Apply(g,0,CommandKind.BeginPrimary);
   Assert.That(g.View(0).EffectMoves.All(m=>m.Path.All(h=>h.Distance(new Hex(7,-8))<=2)),Is.True);
   Apply(g,0,CommandKind.ChooseEffectMove,"skip");Assert.That(g.View(0).GoldTransfers.Count,Is.EqualTo(1));foreach(int? seat in new int?[]{null,1,2,3})Assert.That(g.View(seat).GoldTransfers,Is.Empty);
   Apply(g,0,CommandKind.ChooseGoldTransfer,"1",target:1);Assert.That(g.View(0).EffectMoves.All(m=>m.Path.All(h=>h.Distance(new Hex(7,-8))<=2)),Is.True);ChargeTests.Restore(cat,g);
  }
  [Test] public void SpellBreakRejectsSkillStartWithoutSpendingCardButSecondaryMovementRemains()
  {
   var cat=BattlefieldTests.Catalog();var g=Setup(cat,suppression:true);string before=g.ExportSave();Assert.That(g.View(0).CanBeginPrimary,Is.False);Assert.That(g.Execute(0,Cmd(g,0,CommandKind.BeginPrimary)).Code,Is.EqualTo("primary_restricted"));Assert.That(g.ExportSave(),Is.EqualTo(before));Assert.That(g.View(0).SecondaryMoves,Is.Not.Empty);ChargeTests.Restore(cat,g);
  }
  [Test] public void FrozenWaveStillResumesOriginalAttack()
  {var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine36-wave-discard.json"));var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Pick));Apply(g,3,CommandKind.ForcedDiscard,"sabina-01-拔枪");ChargeTests.Restore(cat,g);}
 }
}

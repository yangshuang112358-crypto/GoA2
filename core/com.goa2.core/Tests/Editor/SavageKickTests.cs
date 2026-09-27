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
    public sealed class SavageKickTests
    {
        internal const string Card="brogan-16-野蛮飞踢";
        internal static GameSession Ready(ContentCatalog cat)
        {
            var g=PunchTests.Ready(cat,Card);Apply(g,0,CommandKind.DebugTeleport,PunchTests.Target,cell:new Hex(0,-3));
            Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(0,-4));return g;
        }
        internal static GameSession Phantasm(ContentCatalog cat)
        {
            var g=LocalGameFactory.Create(cat,"kick-phantasm",new[]{"A","B","C","D"},42,true);
            Apply(g,0,CommandKind.DebugPrepare,"brogan,shargatha,sabina,arien");Apply(g,0,CommandKind.DebugSetGold,"28",target:1);
            Apply(g,0,CommandKind.DebugAdvance,"round");Apply(g,0,CommandKind.ResolveRoundEnd);
            for(int i=0;i<7;i++)Apply(g,1,CommandKind.ChooseUpgrade,g.View(1).UpgradeOptions.First().CardId);
            Apply(g,0,CommandKind.DebugEquipCard,Card,target:0);
            var pos=new[]{new Hex(0,-5),new Hex(0,-4),new Hex(0,-3),new Hex(7,-8)};
            for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);
            string[] cards={Card,"shargatha-00-反击","sabina-07-指挥","arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);
            OpportuneMomentTests.AdvanceTo(g,0);return g;
        }
        private static void Begin(GameSession g)
        {Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectMove,"skip");}
        [Test] public void ExactTextAndVersionGate()
        {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,73),Is.False);c.Text+="友方";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
        [TestCase(0)] [TestCase(1)] [TestCase(2)] public void EnemyHeroCanBePushedZeroOneOrTwoWithoutDamage(int distance)
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,PunchTests.Target,cell:new Hex(-1,-3));Begin(g);
            Assert.That(g.View(0).EffectTargets,Does.Contain("hero:1"));Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");g=ChargeTests.Restore(cat,g);
            Apply(g,0,CommandKind.ChooseEffectMove,distance==0?"skip":"",cell:new Hex(0,-4+distance));ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(0,-4+distance)));
            Assert.That(g.View(0).Events.Any(e=>e.Kind=="AttackCalculated" || e.Kind=="HeroDefeated"),Is.False);
        }
        [Test] public void MovingFirstRecomputesDirectionAndStillAllowsMinions()
        {
            var cat=BattlefieldTests.Catalog();var g=PunchTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-1,-4));
            Apply(g,0,CommandKind.ChooseEffectTarget,PunchTests.Target);Apply(g,0,CommandKind.ChooseEffectMove,"skip");ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(-1,-4)));
        }
        [Test] public void AttackOnlyImmunityAllowsSkillButAllActionImmunityExcludesIt()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Begin(g);var s=new JsonStateCodec().Read(g.ExportSave());
            var effect=new ActiveEffect{SourceCardId="shargatha-17-至死不渝",SourceUnitId="hero:1",ControllerSeat=1,Kind=EffectKind.AttackActionImmunity,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)};s.Effects.Add(effect);
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Contain("hero:1"));effect.Kind=EffectKind.ImmunityAndUnitTraversal;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Does.Not.Contain("hero:1"));
        }
        [Test] public void WrongActorAndFriendlyTargetAreAtomicAndBlockedZeroPushIsLegal()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Begin(g);string before=g.ExportSave();
            foreach(var cmd in new[]{Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:2"),Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")})
            {Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");
            // A minion blocks the next cell, so this is a legal zero-distance push, not an attack.
            Assert.That(g.View(0).Events.Last(e=>e.Kind=="UnitPushed").Path.Count,Is.EqualTo(1));ChargeTests.Restore(cat,g);
        }
        [Test] public void PhantasmCanBePushedThroughAUnitButCannotStopOnItOrTriggerItsPrelude()
        {
            var cat=BattlefieldTests.Catalog();var g=Phantasm(cat);Begin(g);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");g=ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).EffectMoves.Select(m=>m.Destination),Is.EqualTo(new[]{new Hex(0,-2)}));string before=g.ExportSave();
            Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(0,-3))).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
            var push=Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(0,-2));Assert.That(g.Execute(0,push).Accepted,Is.True);before=g.ExportSave();
            Assert.That(g.Execute(0,push).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(0,-2)));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UltimateTriggered"),Is.False);
        }
        [Test] public void PhantasmPushNeedsBudgetToReachAnEmptyEndpointAndOldEngineStillStops()
        {
            var cat=BattlefieldTests.Catalog();var g=Phantasm(cat);var s=new JsonStateCodec().Read(g.ExportSave());var source=s.Units.Single(u=>u.Seat==0);var target=s.Units.Single(u=>u.Seat==1);
            Assert.That(PushRules.AwayFromAdjacent(cat,s,source,target,1).Path.Count,Is.EqualTo(1));
            Assert.That(PushRules.AwayFromAdjacent(cat,s,source,target,2).Path.Last(),Is.EqualTo(new Hex(0,-2)));
            s.EngineVersion=73;Assert.That(PushRules.AwayFromAdjacent(cat,s,source,target,2).Path.Count,Is.EqualTo(1));
        }
        [Test] public void PhantasmCanBePushedThroughTerrainOnlyToAnEmptyFinalCell()
        {
            var cat=BattlefieldTests.Catalog();var g=Phantasm(cat);var s=new JsonStateCodec().Read(g.ExportSave());var source=s.Units.Single(u=>u.Seat==0);var target=s.Units.Single(u=>u.Seat==1);
            var pair=(from c in cat.Cells where !c.Obstacle from t in c.Position.Neighbors() where cat.Cell(t)!=null && !cat.Cell(t).Obstacle
                      let wall=new Hex(t.X*2-c.Position.X,t.Y*2-c.Position.Y) let end=new Hex(t.X*3-c.Position.X*2,t.Y*3-c.Position.Y*2)
                      where cat.Cell(wall)?.Obstacle==true && cat.Cell(end)!=null && !cat.Cell(end).Obstacle
                      select new{Origin=c.Position,Target=t,End=end}).First();
            s.Units.RemoveAll(u=>u.Id!=source.Id && u.Id!=target.Id);source.Position=pair.Origin;target.Position=pair.Target;
            Assert.That(PushRules.AwayFromAdjacent(cat,s,source,target,2).Path.Last(),Is.EqualTo(pair.End));
            Assert.That(PushRules.AwayFromAdjacent(cat,s,source,target,1).Path.Last(),Is.EqualTo(pair.Target));
        }
        [Test] public void TraversalKeepsTheLastEmptyCellWhenTheBudgetEndsInsideAnotherObstacle()
        {
            var cat=BattlefieldTests.Catalog();var g=Phantasm(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(0,-1));
            var s=new JsonStateCodec().Read(g.ExportSave());var result=PushRules.AwayFromAdjacent(cat,s,s.Units.Single(u=>u.Seat==0),s.Units.Single(u=>u.Seat==1),3);
            Assert.That(result.Path,Is.EqualTo(new[]{new Hex(0,-4),new Hex(0,-3),new Hex(0,-2)}));Assert.That(result.StopReason,Is.EqualTo("occupied"));
        }
        [Test] public void PushingSourceDoesNotLendItsTraversalToAnOrdinaryTarget()
        {
            var cat=BattlefieldTests.Catalog();var g=Phantasm(cat);var s=new JsonStateCodec().Read(g.ExportSave());
            var source=s.Units.Single(u=>u.Seat==1);var target=s.Units.Single(u=>u.Seat==0);source.Position=new Hex(0,-5);target.Position=new Hex(0,-4);
            Assert.That(PushRules.AwayFromAdjacent(cat,s,source,target,2).Path.Count,Is.EqualTo(1));
        }
        [Test] public void Previous73MovePushSaveKeepsExactBytes()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine73-crushing-punch-distance.json"));
            var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
        }
    }
}

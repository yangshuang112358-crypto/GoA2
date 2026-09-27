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
    public sealed class PunchTests
    {
        internal const string Card="brogan-13-冲拳",Target="minion:-1,-3";
        internal static GameSession Ready(ContentCatalog cat,string card=Card,bool returnMinion=false)
        {
            var g=LocalGameFactory.Create(cat,"punch",new[]{"A","B","C","D"},42,true);
            Apply(g,0,CommandKind.DebugPrepare,"brogan,wasp,sabina,arien");Apply(g,0,CommandKind.DebugEquipCard,card,target:0);
            var pos=new[]{returnMinion?new Hex(0,-2):new Hex(0,-5),new Hex(6,-8),new Hex(5,-8),new Hex(7,-8)};
            for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:pos[i]);
            Apply(g,0,CommandKind.DebugTeleport,Target,cell:returnMinion?new Hex(0,-3):new Hex(0,-4));
            string[] cards={card,"wasp-07-抵挡屏障","sabina-07-指挥","arien-07-潮水"};for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);
            OpportuneMomentTests.AdvanceTo(g,0);return g;
        }
        [Test] public void ExactBindingAndOldEngineGate()
        {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,71),Is.False);c.Text+="所有单位";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
        [TestCase(0)] [TestCase(1)] [TestCase(2)] public void ChooserCanPushZeroOneOrTwoAndSaveEveryWindow(int distance)
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);g=ChargeTests.Restore(cat,g);
            Apply(g,0,CommandKind.ChooseEffectTarget,Target);g=ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).EffectMoves.Select(m=>m.Destination),Is.EquivalentTo(new[]{new Hex(0,-3),new Hex(0,-2)}));
            Apply(g,0,CommandKind.ChooseEffectMove,distance==0?"skip":"",cell:new Hex(0,-4+distance));ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Units.Single(u=>u.Id==Target).Position,Is.EqualTo(new Hex(0,-4+distance)));
            Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitMoved" || e.Kind=="HeroDefeated"),Is.False);
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitPushed"),Is.EqualTo(distance==0?0:1));
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));
        }
        [Test] public void InvalidTargetsAndDestinationsAreAtomicAndChoicesPrivate()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"minion:1,0",cell:new Hex(-1,-4));
            Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();
            foreach(var cmd in new[]{Cmd(g,0,CommandKind.ChooseEffectTarget,"minion:1,0"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,1,CommandKind.ChooseEffectTarget,Target),Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")})
            {Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            Assert.That(g.View(1).EffectTargets,Is.Empty);Apply(g,0,CommandKind.ChooseEffectTarget,Target);before=g.ExportSave();
            foreach(var cmd in new[]{Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(0,-1)),Cmd(g,1,CommandKind.ChooseEffectMove,"skip"),Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(0,-3),mode:MoveMode.Fast)})
            {Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            Assert.That(g.View(1).EffectMoves,Is.Empty);var push=Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(0,-2));Assert.That(g.Execute(0,push).Accepted,Is.True);
            before=g.ExportSave();Assert.That(g.Execute(0,push).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,g);
        }
        [Test] public void ProtectedHeavyIsExcludedButTheLastHeavyIsAllowed()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());
            var original=s.Units.Single(u=>u.Id==Target);original.Position=new Hex(0,-3);var heavy=s.Units.Single(u=>u.Kind=="heavy" && u.Team==Team.Red);heavy.Position=new Hex(0,-4);
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);s.Units.RemoveAll(u=>u.Kind!="hero" && u.Team==Team.Red && u.Id!=heavy.Id);
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{heavy.Id}));
        }
        [TestCase("obstacle")] [TestCase("map_edge")] [TestCase("occupied")]
        public void BlockedLegalPushRecordsZeroAndDoesNotCrossBlocker(string reason)
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);var s=new JsonStateCodec().Read(g.ExportSave());var source=s.Units.Single(u=>u.Seat==0);var target=s.Units.Single(u=>u.Id==Target);
            var pair=(from c in cat.Cells where !c.Obstacle from n in c.Position.Neighbors() where cat.Cell(n)!=null && !cat.Cell(n).Obstacle
                      let next=new Hex(n.X*2-c.Position.X,n.Y*2-c.Position.Y)
                      where reason=="map_edge"?cat.Cell(next)==null:reason=="obstacle"?cat.Cell(next)?.Obstacle==true:cat.Cell(next)!=null && !cat.Cell(next).Obstacle
                      select new{Origin=c.Position,Target=n,Next=next}).First();
            s.Units.RemoveAll(u=>u.Id!=source.Id && u.Id!=target.Id);source.Position=pair.Origin;target.Position=pair.Target;
            if(reason=="occupied")s.Units.Add(new UnitState{Id="blocker",Kind="melee",Team=Team.Blue,Position=pair.Next});
            var rules=new GameRules();rules.Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});rules.Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectTarget,Value=Target});
            Assert.That(target.Position,Is.EqualTo(pair.Target));Assert.That(s.Events.Single(e=>e.Kind=="UnitPushed").Path.Count,Is.EqualTo(1));Assert.That(s.Events.Last(e=>e.Kind=="PushStopped").Detail,Is.EqualTo(reason));
        }
        [Test] public void MovementBonusAndBoundaryDoNotChangePushDistance()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,Target);var s=new JsonStateCodec().Read(g.ExportSave());
            var initial=GameRules.LegalEffectMoves(cat,s,0).Select(m=>m.Destination).ToArray();s.Players[0].MovementBonus=8;
            s.Effects.Add(new ActiveEffect{SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:0",ControllerSeat=0,Kind=EffectKind.MovementBoundary,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
            Assert.That(GameRules.LegalEffectMoves(cat,s,0).Select(m=>m.Destination),Is.EquivalentTo(initial));
        }
        [Test] public void DisplacedMinionReturnsBeforeCardFinishes()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat,returnMinion:true);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,Target);
            Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(0,-5));Assert.That(g.View(0).Pending.Kind,Is.EqualTo("minion_return"));g=ChargeTests.Restore(cat,g);
            int steps=0;while(g.View(0).Pending?.Kind=="minion_return")
            {Assert.That(++steps,Is.LessThan(8));var pending=g.View(1).Pending;Apply(g,1,CommandKind.ChooseMinionReturn,Target,cell:pending.CandidateCells.First());g=ChargeTests.Restore(cat,g);}
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));
        }
        [Test] public void NoEnemyMinionEndsWithoutOfferingUnavailableMapTokens()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,Target,cell:new Hex(0,-3));Apply(g,0,CommandKind.BeginPrimary);
            Assert.That(g.View(0).Pending?.Kind,Is.Not.EqualTo("effect_target"));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitPushed"),Is.False);ChargeTests.Restore(cat,g);
        }
        [Test] public void Old71PaymentKeepsExactBytes()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine71-energy-explosion-payment.json"));
            var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
        }
    }
}

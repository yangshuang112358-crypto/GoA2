using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
    public sealed class CrushingPunchTests
    {
        internal const string Card="brogan-14-碾压重拳";
        [Test] public void ExactTextAndOldEngineGate()
        {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,72),Is.False);c.Text+="敌方英雄";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
        [TestCase(false)] [TestCase(true)] public void OptionalMoveThenRecomputedPushAndMinionReturn(bool moveFirst)
        {
            var cat=BattlefieldTests.Catalog();var g=PunchTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);g=ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).EffectMoves.All(m=>m.Path.Count<=2),Is.True);
            Apply(g,0,CommandKind.ChooseEffectMove,moveFirst?"":"skip",cell:new Hex(-1,-4));g=ChargeTests.Restore(cat,g);
            Apply(g,0,CommandKind.ChooseEffectTarget,PunchTests.Target);g=ChargeTests.Restore(cat,g);
            var endpoint=moveFirst?new Hex(1,-4):new Hex(0,-2);Apply(g,0,CommandKind.ChooseEffectMove,cell:endpoint);
            Assert.That(g.View(0).Events.Last(e=>e.Kind=="UnitPushed").To,Is.EqualTo(endpoint));
            while(g.View(0).Pending?.Kind=="minion_return")
            {var pending=g.View(1).Pending;Apply(g,1,CommandKind.ChooseMinionReturn,PunchTests.Target,cell:pending.CandidateCells.First());}
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="UnitMoved" && e.CardId==Card),Is.EqualTo(moveFirst?1:0));
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(cat,g);
        }
        [Test] public void MovingAwayLeavesNoPushButDoesNotUndoTheMove()
        {
            var cat=BattlefieldTests.Catalog();var g=PunchTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(0,-6));
            Assert.That(g.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(0,-6)));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UnitPushed"),Is.False);ChargeTests.Restore(cat,g);
        }
        [Test] public void MoveBonusCannotExtendTheOneStepTextMoveAndWrongCommandsAreAtomic()
        {
            var cat=BattlefieldTests.Catalog();var g=PunchTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());s.Players[0].MovementBonus=8;
            Assert.That(GameRules.LegalEffectMoves(cat,s,0).All(m=>m.Path.Count<=2),Is.True);string before=g.ExportSave();
            foreach(var cmd in new[]{Cmd(g,1,CommandKind.ChooseEffectMove,"skip"),Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(0,-7)),Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(-1,-4),mode:MoveMode.Fast)})
            {Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            Assert.That(g.View(1).EffectMoves,Is.Empty);
        }
        [Test] public void BothOptionalDistancesCanBeZeroWithoutDuplicateResolution()
        {
            var cat=BattlefieldTests.Catalog();var g=PunchTests.Ready(cat,Card);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectMove,"skip");Apply(g,0,CommandKind.ChooseEffectTarget,PunchTests.Target);
            var cmd=Cmd(g,0,CommandKind.ChooseEffectMove,"skip");Assert.That(g.Execute(0,cmd).Accepted,Is.True);string after=g.ExportSave();Assert.That(g.Execute(0,cmd).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(after));ChargeTests.Restore(cat,g);
        }
        [Test] public void Previous72PushWindowRetainsBytesAndCapabilities()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine72-punch-distance.json"));
            var g=LocalGameFactory.Restore(cat,save);Assert.That(g.ExportSave(),Is.EqualTo(save));Assert.That(g.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
        }
    }
}

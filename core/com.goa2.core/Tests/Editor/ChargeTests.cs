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
    public sealed class ChargeTests
    {
        internal const string Charge="brogan-01-冲撞";
        internal static readonly Hex Start=new Hex(3,-8), End=new Hex(5,-8);
        internal static GameSession Setup(ContentCatalog catalog,bool minion=true,bool counter=false)
        {
            var game=LocalGameFactory.Create(catalog,"charge",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,tigerclaw,sabina,arien");
            if(counter)Apply(game,0,CommandKind.DebugEquipCard,"tigerclaw-14-近身格挡",target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:Start);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(6,-8));
            if(minion)Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(5,-9));
            string[] cards={Charge,"tigerclaw-07-伺机待发","sabina-07-指挥","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);return game;
        }
        internal static GameSession Restore(ContentCatalog catalog,GameSession game)
        {var save=game.ExportSave();var restored=LocalGameFactory.Restore(catalog,save);Assert.That(restored.ExportSave(),Is.EqualTo(save));return restored;}
        private static void Move(GameSession game){Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,cell:End);}
        [Test]
        public void ExactTextAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Charge);Assert.That(card.PrimaryValue,Is.EqualTo(6));Assert.That(card.Initiative,Is.EqualTo(7));Assert.That(card.SecondaryMovement,Is.EqualTo(3));Assert.That(card.SecondaryDefense,Is.EqualTo(7));
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,25),Is.False);card.Text+="可以跳过。";Assert.That(CombatRules.HasPrimaryProgram(card),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void CompleteStraightMoveThenChooseAnAdjacentHeroOrMinion(bool minion)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("effect_move"));Assert.That(game.View(0).Pending!.Optional,Is.False);Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);
            game=Restore(catalog,game);Assert.That(game.View(0).EffectMoves.Single(m=>m.Destination==End).Path,Is.EqualTo(new[]{Start,new Hex(4,-8),End}));
            Apply(game,0,CommandKind.ChooseEffectMove,cell:End);game=Restore(catalog,game);
            Assert.That(game.View(0).AttackTargets,Is.EquivalentTo(new[]{"hero:1","minion:-1,-3"}));
            var target=minion?"minion:-1,-3":"hero:1";Apply(game,0,CommandKind.ChooseAttackTarget,target);if(!minion)Apply(game,1,CommandKind.DeclineDefense);game=Restore(catalog,game);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(End));Assert.That(game.View(0).Units.Any(u=>u.Id==target),Is.False);Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(minion?2:1));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(1));Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));
        }
        [Test]
        public void InvalidOptionalFastShortLongBentAndWrongSeatChoicesAreAtomic()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);string before=game.ExportSave();
            Assert.That(game.View(1).EffectMoves,Is.Empty);Assert.That(game.View(null).EffectMoves,Is.Empty);
            foreach(var cmd in new[]{Cmd(game,0,CommandKind.ChooseEffectMove,"skip"),Cmd(game,1,CommandKind.ChooseEffectMove,destination:End),Cmd(game,0,CommandKind.ChooseEffectMove,destination:End,mode:MoveMode.Fast),
                Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(4,-8)),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(6,-8)),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(4,-7)),Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:1"),Cmd(game,0,CommandKind.Pass)})
            {Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var valid=Cmd(game,0,CommandKind.ChooseEffectMove,destination:End);Assert.That(game.Execute(0,valid).Accepted,Is.True);game=Restore(catalog,game);var after=game.ExportSave();Assert.That(game.Execute(0,valid).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseEffectMove,destination:End)).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(after));
        }
        [TestCase(false)] [TestCase(true)]
        public void OccupiedIntermediateOrEndCellDoesNotPermitACharge(bool endpoint)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:endpoint?End:new Hex(4,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.Any(m=>m.Destination==End),Is.False);Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(Start));
        }
        [Test]
        public void NoReachableTargetStopsWithoutMovingOrAttackingFromOriginalSpace()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).Events.Any(e=>e.Kind=="CardEffectStopped" && e.Detail=="no_charge_route"),Is.True);Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved" || e.Kind=="AttackDeclared"),Is.False);Restore(catalog,game);
        }
        [Test]
        public void EnemyAlreadyAdjacentDoesNotAllowSkippingTheRequiredMovement()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(4,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Kind,Is.Not.EqualTo("attack_target"));Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void ProtectedHeavyCannotQualifyButUnprotectedHeavyAdvancesAfterCharge(bool unprotected)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));
            if(unprotected)foreach(var u in game.View(0).Units.Where(u=>u.Team==Team.Red && u.Kind!="hero" && u.Kind!="heavy").ToList())Apply(game,0,CommandKind.DebugRemoveMinion,u.Id);
            string heavy=game.View(0).Units.Single(u=>u.Team==Team.Red && u.Kind=="heavy").Id;Apply(game,0,CommandKind.DebugTeleport,heavy,cell:new Hex(6,-8));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.Any(m=>m.Destination==End),Is.EqualTo(unprotected));
            if(unprotected){Apply(game,0,CommandKind.ChooseEffectMove,cell:End);Apply(game,0,CommandKind.ChooseAttackTarget,heavy);Assert.That(game.View(0).BlueMarks,Is.EqualTo(1));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(4));Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));Restore(catalog,game);}
        }
        [Test]
        public void AttackTargetMustStillBeAdjacentAfterTheMove()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Move(game);var before=game.ExportSave();
            foreach(string id in new[]{"hero:0","hero:2","hero:3"}){Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseAttackTarget,id)).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
        }
        [Test]
        public void BonusesDoNotExtendTextMovementOrChangeAdjacentAttack()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].MovementBonus=4;state.Players[0].RangedBonus=4;state.Players[0].RangeBonus=4;state.Players[0].AttackBonus=2;
            game=new GameSession(catalog,codec,state);Move(game);Assert.That(game.View(0).AttackTargets,Is.EquivalentTo(new[]{"hero:1","minion:-1,-3"}));Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(0).Attack!.FinalAttack,Is.EqualTo(8));Assert.That(game.View(0).Events.Single(e=>e.Kind=="UnitMoved").Path.Count,Is.EqualTo(3));
        }
        [Test]
        public void StaticBoundaryFiltersMandatoryRoutesWithoutSuppressingTheAttackCard()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());
            state.Units.Single(u=>u.Seat==3).Position=new Hex(6,-7);
            state.Effects.Add(new ActiveEffect{Id="static",Kind=EffectKind.MovementBoundary,SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:3",ControllerSeat=3,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            game=new GameSession(catalog,codec,state);Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).EffectMoves.Any(m=>m.Destination==End),Is.False);Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(Start));
        }
        [Test]
        public void MeleeCounterRunsAfterTheCompletedChargeAndKeepsItsNewPosition()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,counter:true);Move(game);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.Defend,"tigerclaw-14-近身格挡");game=Restore(catalog,game);
            Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("forced_discard"));Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(End));Apply(game,0,CommandKind.ForcedDiscard,"brogan-00-猛攻");Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));Restore(catalog,game);
        }
        [TestCase(MoveMode.Secondary)] [TestCase(MoveMode.Fast)]
        public void MovementReplacementDoesNotRunChargeText(MoveMode mode)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            if(mode==MoveMode.Fast)Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:game.View(0).DebugTeleports["hero:0"].First(h=>catalog.Cell(h)!.Region=="blueFountain"));
            var options=mode==MoveMode.Fast?game.View(0).FastMoves:game.View(0).SecondaryMoves;Apply(game,0,CommandKind.Move,cell:options.First().Destination,mode:mode);
            Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).Events.Any(e=>e.Kind=="PrimaryActionStarted" || e.Kind=="AttackDeclared" || e.Kind=="EffectMoveChoiceRequired"),Is.False);Restore(catalog,game);
        }
        [Test]
        public void CrystalVictoryEndsAfterTheOneChargeAttack()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);Move(game);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(1));Restore(catalog,game);
        }
        [Test]
        public void FrozenShadowStepSwapDoesNotAcquireChargeProgram()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine25-shadowstep-swap.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Charge));Apply(game,1,CommandKind.ChooseCardSwap,"tigerclaw-00-瞬闪打击");Restore(catalog,game);
        }
    }
}

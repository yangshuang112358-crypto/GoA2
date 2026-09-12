using System;
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
    public sealed class SidestepTests
    {
        internal const string Step="tigerclaw-15-侧步";
        internal static GameSession Setup(ContentCatalog catalog,bool push=false,string ally="brogan")
        {
            var game=LocalGameFactory.Create(catalog,"sidestep",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"sabina,tigerclaw,"+ally+",arien");Apply(game,0,CommandKind.DebugEquipCard,Step,target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(push?7:8,-8));
            var cards=new[]{push?"sabina-00-近身射击":"sabina-01-拔枪","tigerclaw-07-伺机待发",ally=="wasp"?"wasp-07-抵挡屏障":"brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);return game;
        }
        private static void Attack(GameSession game){Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");}
        private static GameSession Restore(ContentCatalog catalog,GameSession game){string save=game.ExportSave();var result=LocalGameFactory.Restore(catalog,save);Assert.That(result.ExportSave(),Is.EqualTo(save));return result;}
        [Test]
        public void ExactContractAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Step);Assert.That(card.PrimaryFamily,Is.EqualTo("defense"));Assert.That(card.PrimaryValue,Is.Zero);Assert.That(card.SecondaryMovement,Is.EqualTo(3));Assert.That(card.Initiative,Is.EqualTo(11));
            Assert.That(CombatRules.HasDefenseProgram(card),Is.True);Assert.That(CombatRules.HasDefenseProgram(card,23),Is.False);card.Text+="获得免疫。";Assert.That(CombatRules.HasDefenseProgram(card),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void BlocksRangedThenMovesFromThePositionAfterOriginalAttackText(bool push)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,push);Attack(game);Assert.That(game.View(1).DefenseOptions.Any(o=>o.CardId==Step && o.Block),Is.True);Apply(game,1,CommandKind.Defend,Step);game=Restore(catalog,game);
            var view=game.View(1);Assert.That(view.Pending!.Kind,Is.EqualTo("effect_move"));Assert.That(view.Pending.ResumeAt,Is.EqualTo("defense_response_move"));Assert.That(view.Pending.ChooserSeat,Is.EqualTo(1));Assert.That(view.Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(8,-8)));
            Assert.That(game.View(0).EffectMoves,Is.Empty);Assert.That(game.View(null).EffectMoves,Is.Empty);Assert.That(game.View(0).Pending!.Source,Is.Empty);Assert.That(view.Pending.Source,Is.EqualTo(Step));
            Assert.That(view.EffectMoves,Is.Not.Empty);Assert.That(view.EffectMoves.All(m=>m.Path.Count==3 && m.Path[0]==new Hex(8,-8) && m.Path[0].IsInStraightLineWith(m.Destination)),Is.True);
            var option=view.EffectMoves.First();Apply(game,1,CommandKind.ChooseEffectMove,cell:option.Destination);game=Restore(catalog,game);var all=game.View(1);
            Assert.That(all.Units.Single(u=>u.Seat==1).Position,Is.EqualTo(option.Destination));Assert.That(all.Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));Assert.That(all.Events.Count(e=>e.Kind=="UnitPushed"),Is.EqualTo(push?1:0));Assert.That(all.Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));Assert.That(all.Pending,Is.Null);
            var move=game.View(null).Events.Single(e=>e.Kind=="UnitMoved");Assert.That(move.CardId,Is.Null);Assert.That(move.Seat,Is.EqualTo(1));Assert.That(move.Path,Is.EqualTo(option.Path));Assert.That(all.Events.Single(e=>e.Kind=="DefenseMoveResolved").CardId,Is.EqualTo(Step));Assert.That(game.View(0).Events.Any(e=>e.CardId==Step),Is.False);
            Assert.That(all.Events.First(e=>e.Kind=="AttackResolved").Sequence,Is.LessThan(move.Sequence));if(push)Assert.That(all.Events.Single(e=>e.Kind=="UnitPushed").Sequence,Is.LessThan(move.Sequence));
        }
        [Test]
        public void SkipKeepsBlockAndDoesNotMoveOrDiscardAttackerCards()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);int hand=game.View(0).Players[0].HandCount;Attack(game);Apply(game,1,CommandKind.Defend,Step);Apply(game,1,CommandKind.ChooseEffectMove,"skip");game=Restore(catalog,game);
            Assert.That(game.View(0).Players[0].HandCount,Is.EqualTo(hand));Assert.That(game.View(0).RedCrystal,Is.EqualTo(7));Assert.That(game.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(8,-8)));Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved" || e.Kind=="ForcedDiscardRequired"),Is.False);Assert.That(game.View(0).Pending,Is.Null);
        }
        [Test]
        public void IllegalActorShortMoveFastMoveAndBendAreAtomic()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);Apply(game,1,CommandKind.Defend,Step);var valid=game.View(1).EffectMoves.First().Destination;string before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,0,CommandKind.ChooseEffectMove,"skip"),Cmd(game,1,CommandKind.ChooseEffectMove,destination:new Hex(8,-7)),Cmd(game,1,CommandKind.ChooseEffectMove,destination:new Hex(9,-7)),Cmd(game,1,CommandKind.ChooseEffectMove,destination:valid,mode:MoveMode.Fast),Cmd(game,1,CommandKind.ChooseEffectMove,"invalid")})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var good=Cmd(game,1,CommandKind.ChooseEffectMove,destination:valid);Assert.That(game.Execute(1,good).Accepted,Is.True);var after=game.ExportSave();Assert.That(game.Execute(1,good).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
        }
        [TestCase(1,0)] [TestCase(0,1)] [TestCase(-1,1)] [TestCase(-1,0)] [TestCase(0,-1)] [TestCase(1,-1)]
        public void StraightMoveUsesAllSixDirectionsWhenClear(int dx,int dy)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());var occupied=state.Units.Select(u=>u.Position).ToList();
            var origin=catalog.Cells.First(c=>!c.Obstacle && !occupied.Contains(c.Position) && catalog.Cell(new Hex(c.Position.X+dx,c.Position.Y+dy))?.Obstacle==false && catalog.Cell(new Hex(c.Position.X+2*dx,c.Position.Y+2*dy))?.Obstacle==false && !occupied.Contains(new Hex(c.Position.X+dx,c.Position.Y+dy)) && !occupied.Contains(new Hex(c.Position.X+2*dx,c.Position.Y+2*dy))).Position;
            // Isolated defense state tests geometry; ordinary activation and replay use the session cases above.
            state.Units.Single(u=>u.Seat==1).Position=origin;new GameRules().Apply(catalog,state,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value=Step});var end=new Hex(origin.X+2*dx,origin.Y+2*dy);
            Assert.That(GameRules.LegalEffectMoves(catalog,state,1).Any(m=>m.Destination==end && m.Path[1]==new Hex(origin.X+dx,origin.Y+dy)),Is.True);
        }
        [Test]
        [TestCase(false)] [TestCase(true)]
        public void UnitOnIntermediateOrFinalCellBlocksTheWholeStraightRoute(bool finalCell)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(8,finalCell?-10:-9));Attack(game);Apply(game,1,CommandKind.Defend,Step);
            Assert.That(game.View(1).EffectMoves.Any(m=>m.Destination==new Hex(8,-10)),Is.False);Assert.That(game.View(1).EffectMoves.All(m=>m.Path.All(p=>catalog.Cell(p)?.Obstacle==false)),Is.True);Restore(catalog,game);
        }
        [Test]
        public void NoCompletePathSkipsButRetainsSuccessfulDefense()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());var hero=state.Units.Single(u=>u.Seat==1);
            foreach(var cell in hero.Position.Neighbors().Where(p=>catalog.Cell(p)?.Obstacle==false && !state.Units.Any(u=>u.Position==p)))state.Units.Add(new UnitState{Id="blocker:"+cell,Kind="melee",Team=Team.Red,Position=cell});
            new GameRules().Apply(catalog,state,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value=Step});Assert.That(state.Pending,Is.Null);Assert.That(state.Events.Any(e=>e.Kind=="EffectMoveChoiceRequired"),Is.False);Assert.That(state.RedCrystal,Is.EqualTo(7));Assert.That(state.Events.Any(e=>e.Kind=="UnitMoved"),Is.False);
        }
        [Test]
        public void NonRangedAndUnblockableAttacksRejectSidestep()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());var rules=new GameRules();
            foreach(bool ranged in new[]{false,true})
            {state.Execution!.Attack!.Ranged=ranged;state.Execution.Attack.Unblockable=ranged;string before=new JsonStateCodec().Write(state);Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o=>o.CardId==Step),Is.False);Assert.Throws<RuleViolation>(()=>rules.Apply(catalog,state,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value=Step}));Assert.That(new JsonStateCodec().Write(state),Is.EqualTo(before));}
        }
        [Test]
        public void StaticBoundaryAndMovementBonusesUseNormalMovementRules()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,ally:"wasp");Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-7));Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());
            state.Players[1].MovementBonus=4;state.Effects.Add(new ActiveEffect{Id="static",Kind=EffectKind.MovementBoundary,SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:2",ControllerSeat=2,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            new GameRules().Apply(catalog,state,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value=Step});var options=GameRules.LegalEffectMoves(catalog,state,1);
            Assert.That(options.All(m=>m.Path.Count==3 && m.Path.Skip(1).All(p=>p.Distance(new Hex(6,-7))<=2)),Is.True);Assert.That(options.Any(m=>m.Destination==new Hex(8,-10)),Is.False);
        }
        [Test]
        public void FrozenMightyTargetCellRemainsCompatible()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine23-mighty-defense.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(1).SupportedDefenseCards,Does.Not.Contain(Step));Apply(game,1,CommandKind.DeclineDefense);game=Restore(catalog,game);Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(7,-8)));
        }
    }
}

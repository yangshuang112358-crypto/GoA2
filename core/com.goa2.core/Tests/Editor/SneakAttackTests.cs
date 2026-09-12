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
    public sealed class SneakAttackTests
    {
        internal const string Sneak="tigerclaw-02-偷袭";
        private const CommandKind MoveChoice=CommandKind.ChooseEffectMove;
        internal static GameSession Setup(ContentCatalog catalog,string defender="wasp",int engine=GameState.CurrentEngineVersion)
        {
            var game=LocalGameFactory.Create(catalog,"sneak",new[]{"A","B","C","D"},42,true,engine);
            Apply(game,0,CommandKind.DebugPrepare,"tigerclaw,"+defender+",brogan,"+(defender=="arien" ? "wasp" : "arien"));
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));
            return game;
        }
        internal static void Select(GameSession game,ContentCatalog catalog)
        {
            for(int seat=0;seat<4;seat++)
            {
                string id=seat==0 ? Sneak : game.View(seat).OwnCards.Select(c=>catalog.Card(c.CardId))
                    .Where(c=>c.PrimaryFamily!="defense" && c.Color!="gold").OrderBy(c=>c.Initiative).ThenBy(c=>c.Id,StringComparer.Ordinal).First().Id;
                Apply(game,seat,CommandKind.SelectCard,id);
            }
            Assert.That(game.View(null).ActiveSeat,Is.EqualTo(0));
        }
        internal static void Attack(GameSession game,string target="hero:1")
        { Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,target); }
        private static GameSession Pending(ContentCatalog catalog)
        {
            var game=Setup(catalog);Select(game,catalog);Attack(game);
            game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,1,CommandKind.DeclineDefense);return game;
        }
        [Test]
        public void FormalCardIsSupportedOnlyInNewEngineAndHasExactValues()
        {
            var card=BattlefieldTests.Catalog().Card(Sneak);
            Assert.That(card.PrimaryCategory,Is.EqualTo("攻击"));Assert.That(card.PrimaryValue,Is.EqualTo(3));
            Assert.That(card.SecondaryMovement,Is.EqualTo(5));Assert.That(card.SecondaryDefense,Is.EqualTo(3));
            Assert.That(card.Initiative,Is.EqualTo(9));Assert.That(card.Subtype,Is.Null);
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);
            Assert.That(CombatRules.HasPrimaryProgram(card,10),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void SuccessfulAttackOffersOneOptionalMoveAndRestoresWithoutDoubleResolution(bool skip)
        {
            var catalog=BattlefieldTests.Catalog();var game=Pending(catalog);var view=game.View(0);
            Assert.That(view.Pending!.Kind,Is.EqualTo("effect_move"));Assert.That(view.Pending.ChooserSeat,Is.EqualTo(0));
            Assert.That(view.Pending.Source,Is.EqualTo(Sneak));Assert.That(view.Pending.Optional,Is.True);
            Assert.That(view.Players[1].AwaitingRespawn,Is.True);Assert.That(view.Players[0].Gold,Is.EqualTo(1));
            Assert.That(view.Players[0].Revealed.Single().Zone,Is.EqualTo(CardZone.PlayedUnresolved));
            Assert.That(view.CanPass,Is.False);Assert.That(view.SecondaryMoves,Is.Empty);Assert.That(view.FastMoves,Is.Empty);
            var destination=view.Pending.CandidateCells.First();var origin=view.Units.Single(u=>u.Seat==0).Position;
            game=LocalGameFactory.Restore(catalog,game.ExportSave());
            var command=Cmd(game,0,MoveChoice,skip ? "skip" : "",destination:destination);
            Assert.That(game.Execute(0,command).Accepted,Is.True);
            view=game.View(0);
            Assert.That(view.Units.Single(u=>u.Seat==0).Position,Is.EqualTo(skip ? origin : destination));
            Assert.That(view.Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(skip ? 0 : 1));
            Assert.That(view.Events.Count(e=>e.Kind=="PrimaryActionStarted"),Is.EqualTo(1));
            Assert.That(view.Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(view.Events.Count(e=>e.Kind=="CardResolved" && e.Seat==0),Is.EqualTo(1));
            Assert.That(view.Events.FindIndex(e=>e.Kind=="AttackResolved"),Is.LessThan(view.Events.FindIndex(e=>e.Kind=="EffectMoveChoiceRequired")));
            if(!skip)
            {
                var moved=view.Events.Single(e=>e.Kind=="UnitMoved");
                Assert.That(moved.CardId,Is.EqualTo(Sneak));Assert.That(moved.Path,Is.EqualTo(new[]{origin,destination}));
                Assert.That(moved.Detail,Is.EqualTo("CardText"));
            }
            string after=game.ExportSave();game=LocalGameFactory.Restore(catalog,after);
            Assert.That(game.Execute(0,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.Execute(0,Cmd(game,0,MoveChoice,destination:origin)).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(after));
        }
        [TestCase("arien","arien-13-挑战者",false)]
        [TestCase("wasp","wasp-00-闪耀之刃",true)]
        [TestCase("sabina","sabina-08-带头冲锋",false)]
        public void LegalDifferentHeroDefensesStillAllowAfterAttackMovement(string hero,string defense,bool defeated)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,hero);
            Apply(game,0,CommandKind.DebugEquipCard,defense,target:1);
            if(hero=="sabina")
            {
                string minion=game.View(null).Units.First(u=>u.Kind=="melee" && u.Team==Team.Red).Id;
                Apply(game,0,CommandKind.DebugTeleport,minion,cell:game.View(0).DebugTeleports[minion].First(h=>h.Distance(new Hex(8,-10))==1));
            }
            Select(game,catalog);Attack(game);
            Apply(game,1,CommandKind.Defend,defense);
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("effect_move"));
            Assert.That(game.View(0).Players[1].AwaitingRespawn,Is.EqualTo(defeated));
            Assert.That(game.View(1).OwnCards.Single(c=>c.CardId==defense).Zone,Is.EqualTo(CardZone.Discarded));
            game=LocalGameFactory.Restore(catalog,game.ExportSave());Apply(game,0,MoveChoice,"skip");
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
        }
        [Test]
        public void RangeAndMovementBonusesDoNotExpandTextMoveOrAdjacentAttack()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Select(game,catalog);Attack(game);
            Apply(game,1,CommandKind.DeclineDefense);
            var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());
            state.Players[0].MovementBonus=2;state.Players[0].RangeBonus=5;state.Players[0].RangedBonus=5;
            // Deliberately seeded host state checks numeric policy, not replay provenance.
            game=new GameSession(catalog,codec,state);
            var origin=state.Units.Single(u=>u.Seat==0).Position;
            Assert.That(game.View(0).Pending!.CandidateCells.All(h=>h.Distance(origin)==1),Is.True);
            string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,MoveChoice,destination:new Hex(9,-10))).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
            Apply(game,0,MoveChoice,cell:game.View(0).Pending!.CandidateCells.First());
            Assert.That(game.View(null).Units.Single(u=>u.Seat==0).Position.Distance(origin),Is.EqualTo(1));
            game=Setup(catalog);Select(game,catalog);state=codec.Read(game.ExportSave());state.Players[0].MovementBonus=2;
            state.Players[0].RangedBonus=5;state.Players[0].RangeBonus=5;
            game=new GameSession(catalog,codec,state);
            Assert.That(game.View(0).SecondaryMoves.Any(m=>m.Path.Count>6),Is.True);
            Assert.That(game.View(0).AttackRange,Is.EqualTo(1));
        }
        [Test]
        public void WrongIdentityStaleRevisionInvalidInputAndOtherActionsAreAtomic()
        {
            var catalog=BattlefieldTests.Catalog();var game=Pending(catalog);
            string before=game.ExportSave();var origin=game.View(0).Units.Single(u=>u.Seat==0).Position;
            var destination=game.View(0).Pending!.CandidateCells.First();
            foreach(int seat in new[]{1,2,3}) Assert.That(game.Execute(seat,Cmd(game,seat,MoveChoice,destination:destination)).Accepted,Is.False);
            var command=Cmd(game,0,MoveChoice,destination:destination);
            Assert.That(game.Execute(1,command).Code,Is.EqualTo("unauthorized"));command.ExpectedRevision--;
            Assert.That(game.Execute(0,command).Code,Is.EqualTo("stale_revision"));
            foreach(var point in new[]{origin,new Hex(999,999),new Hex(9,-10)})
                Assert.That(game.Execute(0,Cmd(game,0,MoveChoice,destination:point)).Accepted,Is.False);
            Assert.That(game.Execute(0,Cmd(game,0,MoveChoice,"other",destination:destination)).Accepted,Is.False);
            Assert.That(game.Execute(0,Cmd(game,0,MoveChoice,destination:destination,mode:MoveMode.Fast)).Accepted,Is.False);
            foreach(var kind in new[]{CommandKind.Pass,CommandKind.Move,CommandKind.BeginPrimary,CommandKind.DebugTeleport})
                Assert.That(game.Execute(0,Cmd(game,0,kind,"hero:0",destination:destination)).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
        }
        [TestCase(false)] [TestCase(true)]
        public void MissingPathOrAllAdjacentOccupiedSkipsOnlyTheOptionalMove(bool obstacles)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,"arien");Select(game,catalog);Attack(game);
            var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());
            var origin=state.Units.Single(u=>u.Seat==0).Position;
            foreach(var cell in origin.Neighbors().Where(h=>catalog.Cell(h)!=null && !state.Units.Any(u=>u.Position==h)))
                if(obstacles) catalog.Cell(cell)!.Obstacle=true;
                else state.Units.Add(new UnitState{Id="test-block:"+cell,Kind="melee",Team=Team.Blue,Position=cell});
            game=new GameSession(catalog,codec,state);Apply(game,1,CommandKind.Defend,"arien-13-挑战者");
            Assert.That(game.View(null).Pending?.Kind,Is.Not.EqualTo("effect_move"));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="EffectMoveSkipped"),Is.EqualTo(1));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="CardResolved" && e.Seat==0),Is.EqualTo(1));
            Assert.That(game.View(null).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(origin));
        }
        [Test]
        public void CrystalVictoryStopsBeforeAnyOptionalMove()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);Select(game,catalog);Attack(game);Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(null).Phase,Is.EqualTo(Phase.Finished));Assert.That(game.View(null).Pending,Is.Null);
            Assert.That(game.View(null).Events.Any(e=>e.Kind=="EffectMoveChoiceRequired"),Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void OrdinaryMinionKillPaysOnceThenMovesToTheFreedCell()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:game.View(0).DebugTeleports["hero:1"].First(h=>h.Distance(new Hex(7,-10))>3));
            string victim=game.View(null).Units.First(u=>u.Kind=="melee" && u.Team==Team.Red).Id;
            Apply(game,0,CommandKind.DebugTeleport,victim,cell:new Hex(8,-10));Select(game,catalog);Attack(game,victim);
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("effect_move"));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));
            game=LocalGameFactory.Restore(catalog,game.ExportSave());Apply(game,0,MoveChoice,cell:new Hex(8,-10));
            Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));Assert.That(game.View(0).Units.Any(u=>u.Id==victim),Is.False);
        }
        [TestCase(MoveMode.Secondary)] [TestCase(MoveMode.Fast)]
        public void MovementReplacementDoesNotRunAttackOrTextMove(MoveMode mode)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            if(mode==MoveMode.Fast) Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:game.View(0).DebugTeleports["hero:0"].First(h=>catalog.Cell(h)!.Region=="blueFountain"));
            Select(game,catalog);var options=mode==MoveMode.Fast ? game.View(0).FastMoves : game.View(0).SecondaryMoves;
            Apply(game,0,CommandKind.Move,cell:options.First().Destination,mode:mode);
            Assert.That(game.View(null).Events.Any(e=>e.Kind=="AttackDeclared" || e.Kind=="EffectMoveChoiceRequired"),Is.False);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Sneak).Zone,Is.EqualTo(CardZone.PlayedResolved));
        }
        [Test]
        public void EffectMoveProjectionIsOwnerOnlyAndCannotMutateAuthority()
        {
            var game=Pending(BattlefieldTests.Catalog());var view=game.View(0);
            Assert.That(view.EffectMoves,Is.Not.Empty);
            foreach(int? seat in new int?[]{null,1,2,3}) Assert.That(game.View(seat).EffectMoves,Is.Empty);
            string before=game.ExportSave();view.EffectMoves[0].Path.Clear();view.EffectMoves.Clear();view.Pending!.CandidateCells.Clear();
            Assert.That(game.ExportSave(),Is.EqualTo(before));Assert.That(game.View(0).EffectMoves,Is.Not.Empty);
        }
        [TestCase(false)] [TestCase(true)]
        public void HeavyKillResumesOnceAfterFrontlineAndOptionalSpawn(bool blockedSpawn)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            foreach(var unit in game.View(null).Units.Where(u=>u.Team==Team.Red && u.Kind!="hero" && u.Kind!="heavy").ToList())
                Apply(game,0,CommandKind.DebugRemoveMinion,unit.Id);
            var heavy=game.View(null).Units.Single(u=>u.Team==Team.Red && u.Kind=="heavy");
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:game.View(0).DebugTeleports["hero:0"].First(h=>h.Distance(heavy.Position)==1));
            if(blockedSpawn) Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(2,-7));
            Select(game,catalog);Attack(game,heavy.Id);
            if(blockedSpawn) Assert.That(game.View(null).Pending!.Kind,Is.EqualTo("minion_spawn"));
            while(game.View(null).Pending?.Kind=="minion_spawn")
            {
                game=LocalGameFactory.Restore(catalog,game.ExportSave());int seat=game.View(null).Pending!.ChooserSeat;
                var spawn=game.View(seat).SpawnChoices.First(p=>p.Value.Count>0);
                Apply(game,seat,CommandKind.ChooseMinionSpawn,spawn.Key,cell:spawn.Value.First());
            }
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("effect_move"));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="EffectMoveChoiceRequired"),Is.EqualTo(1));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(4));
            game=LocalGameFactory.Restore(catalog,game.ExportSave());Apply(game,0,MoveChoice,"skip");
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.Seat==0),Is.EqualTo(1));
        }
        [TestCase(false)] [TestCase(true)]
        public void StaticBoundaryConstrainsTextMovementWhileSilenceAllowsAttack(bool silence)
        {
            var catalog=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(catalog,"sneak-aura",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,silence ? "arien,tigerclaw,brogan,wasp" : "wasp,tigerclaw,brogan,arien");
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-10));
            string[] cards={silence ? "arien-06-打断施法" : "wasp-06-静电封锁",Sneak,"brogan-06-铜墙铁壁",silence ? "wasp-07-抵挡屏障" : "arien-07-潮水"};
            for(int seat=0;seat<4;seat++) Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(1).CanBeginPrimary,Is.True);
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:2");
            Apply(game,2,CommandKind.DeclineDefense);
            game=LocalGameFactory.Restore(catalog,game.ExportSave());
            var options=game.View(1).EffectMoves;Assert.That(options,Is.Not.Empty);
            Assert.That(options.Any(m=>m.Destination.Distance(new Hex(7,-10))>2),Is.EqualTo(silence));
            string before=game.ExportSave();
            if(!silence)
            {
                var outside=catalog.Cells.First(c=>!c.Obstacle && c.Position.Distance(new Hex(8,-9))==1 && c.Position.Distance(new Hex(7,-10))>2 && !game.View(null).Units.Any(u=>u.Position==c.Position)).Position;
                Assert.That(game.Execute(1,Cmd(game,1,MoveChoice,destination:outside)).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));
            }
            Apply(game,1,MoveChoice,cell:options.First().Destination);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void NoTargetCannotAwardFreeMovementAndRangedBarriersCannotBlockThisAttack()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Select(game,catalog);Attack(game);
            Assert.That(game.View(1).DefenseOptions.Where(d=>d.Primary && d.Block),Is.Empty);
            game=Setup(catalog);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:game.View(0).DebugTeleports["hero:1"].First(h=>h.Distance(new Hex(7,-10))>3));
            Select(game,catalog);Assert.That(game.View(0).AttackTargets,Is.Empty);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="EffectMoveChoiceRequired"),Is.False);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Sneak).Zone,Is.EqualTo(CardZone.PlayedResolved));
        }
        [Test]
        public void Engine10FixtureKeepsCounterChoiceAndCannotGainNewCardByReplay()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine10-riposte-choice.json"));
            var game=LocalGameFactory.Restore(catalog,save);
            Assert.That(game.View(0).EngineVersion,Is.EqualTo(10));Assert.That(game.View(0).CanDeclineRetaliationDiscard,Is.True);
            Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Sneak));
            Apply(game,0,CommandKind.DeclineRetaliationDiscard);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
    }
}

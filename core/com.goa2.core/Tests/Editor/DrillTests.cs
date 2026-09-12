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
    public sealed class DrillTests
    {
        internal const string Drill="sabina-17-演练",Melee="minion:1,0",Heavy="minion:3,1";
        internal static GameSession Setup(ContentCatalog catalog,string interference="",int engine=GameState.CurrentEngineVersion,string card=Drill)
        {
            bool silence=interference=="silence";
            var game=LocalGameFactory.Create(catalog,"drill",new[]{"A","B","C","D"},42,true,engine);Apply(game,0,CommandKind.DebugPrepare,"sabina,tigerclaw,brogan,"+(silence?"arien":"wasp"));Apply(game,0,CommandKind.DebugEquipCard,card,target:0);Apply(game,0,CommandKind.DebugEquipCard,"brogan-02-投掷飞斧",target:2);Apply(game,0,CommandKind.DebugEquipCard,"brogan-10-吟游诗人",target:2);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(3,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-8));Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(5,-8));Apply(game,0,CommandKind.DebugTeleport,Heavy,cell:new Hex(6,-9));
            if(interference!="")Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(2,-7));
            string[] cards={card,"tigerclaw-07-伺机待发","brogan-06-铜墙铁壁",silence?"arien-06-打断施法":interference=="static"?"wasp-06-静电封锁":"wasp-07-抵挡屏障"};for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);return game;
        }
        internal static void Activate(GameSession game,bool stay=false)
        {Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,stay?"skip":"",cell:new Hex(4,-8));}
        internal static void NextAttack(GameSession game,string brogan="brogan-00-猛攻",string sabina="sabina-07-指挥",string wasp="wasp-01-电击",int actor=2)
        {
            Apply(game,0,CommandKind.DebugAdvance,"turn");Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Planning));string[] cards={sabina,"tigerclaw-02-偷袭",brogan,wasp};for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            for(int step=0;step<8 && game.View(0).ActiveSeat!=actor;step++)
            {var v=game.View(0);if(v.Phase==Phase.InitiativeChoice)Apply(game,v.Pending!.ChooserSeat,CommandKind.ChooseInitiative,target:v.Pending.CandidateSeats.Contains(actor)?actor:v.Pending.CandidateSeats.First());else Apply(game,v.ActiveSeat!.Value,CommandKind.Pass);}
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(actor));
        }
        [Test]
        public void ExactMovementCardAndVersionGate()
        {
            var c=BattlefieldTests.Catalog().Card(Drill);Assert.That(c.PrimaryFamily,Is.EqualTo("movement"));Assert.That(c.PrimaryValue,Is.EqualTo(3));Assert.That(c.SecondaryMovement,Is.Null);Assert.That(c.SecondaryDefense,Is.EqualTo(3));Assert.That(c.SubtypeValue,Is.EqualTo(2));Assert.That(c.Initiative,Is.EqualTo(9));
            Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,29),Is.False);c.Text+="所有攻击。";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void MovingOrStayingCreatesOneRoundEffectOnlyAfterTheChoice(bool stay)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Assert.That(game.View(0).SecondaryMoves,Is.Empty);Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("effect_move"));Assert.That(game.View(0).Pending!.Optional,Is.True);Assert.That(game.View(0).Effects,Is.Empty);game=ChargeTests.Restore(catalog,game);
            Assert.That(game.View(0).EffectMoves.All(m=>m.Path.Count<=4),Is.True);Apply(game,0,CommandKind.ChooseEffectMove,stay?"skip":"",cell:new Hex(4,-8));game=ChargeTests.Restore(catalog,game);
            var e=game.View(0).Effects.Single();Assert.That(e.SourceCardId,Is.EqualTo(Drill));Assert.That(e.Duration,Is.EqualTo(EffectDuration.ThisRound));Assert.That(e.Window.EndTurn,Is.EqualTo(4));Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(stay?3:4,-8)));Assert.That(game.View(0).Events.Count(v=>v.Kind=="CardResolved"),Is.EqualTo(1));
        }
        [Test]
        public void InvalidOrDuplicateMovementDoesNotActivateTheAuraTwice()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);var before=game.ExportSave();Assert.That(game.View(1).EffectMoves,Is.Empty);
            foreach(var c in new[]{Cmd(game,1,CommandKind.ChooseEffectMove,"skip"),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(4,-8),mode:MoveMode.Fast),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(5,-8)),Cmd(game,0,CommandKind.Move,destination:new Hex(4,-8)),Cmd(game,0,CommandKind.Pass)})
            {Assert.That(game.Execute(c.ActorSeat,c).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var move=Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(4,-8));Assert.That(game.Execute(0,move).Accepted,Is.True);game=ChargeTests.Restore(catalog,game);before=game.ExportSave();Assert.That(game.Execute(0,move).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(before));Assert.That(game.View(0).Effects.Count,Is.EqualTo(1));
        }
        [Test]
        public void PrimaryMovementUsesItsIconBonus()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].MovementBonus=2;game=new GameSession(catalog,codec,state);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.Any(m=>m.Path.Count==6),Is.True);Assert.That(game.View(0).EffectMoves.All(m=>m.Path.Count<=6),Is.True);
        }
        [Test]
        public void NoLegalMovementStillAllowsTheRoundEffect()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());var pos=state.Units.Single(u=>u.Seat==0).Position;
            foreach(var cell in pos.Neighbors().Where(h=>catalog.Cell(h)?.Obstacle==false && !state.Units.Any(u=>u.Position==h)))state.Units.Add(new UnitState{Id="block:"+cell,Kind="melee",Team=Team.Blue,Position=cell});game=new GameSession(catalog,codec,state);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).Effects.Single().SourceCardId,Is.EqualTo(Drill));Assert.That(game.View(0).Events.Any(e=>e.Kind=="UnitMoved"),Is.False);
        }
        [Test]
        public void FastTravelReplacesTheWholeActionAndCreatesNoAura()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:game.View(0).DebugTeleports["hero:0"].First(h=>catalog.Cell(h)!.Region=="blueFountain"));Apply(game,0,CommandKind.Move,cell:game.View(0).FastMoves.First().Destination,mode:MoveMode.Fast);Assert.That(game.View(0).Effects,Is.Empty);Assert.That(game.View(0).Events.Any(e=>e.Kind=="PrimaryActionStarted"),Is.False);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void AllyBasicAttackGainsRangedSupportFromMeleeAndProtectedHeavy()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Activate(game);game=ChargeTests.Restore(catalog,game);NextAttack(game);Apply(game,2,CommandKind.BeginPrimary);Apply(game,2,CommandKind.ChooseAttackTarget,"hero:1");game=ChargeTests.Restore(catalog,game);
            Assert.That(game.View(2).Attack!.FinalAttack,Is.EqualTo(5));Assert.That(game.View(2).Attack!.EnemySupportSources,Is.EquivalentTo(new[]{Melee,Heavy}));Assert.That(game.View(0).Units.Single(u=>u.Id==Heavy).Kind,Is.EqualTo("heavy"));Assert.That(game.View(0).RemovableMinions,Does.Not.Contain(Heavy));
        }
        [Test]
        public void OrdinaryAttackDoesNotReceiveTheBasicOnlyConversion()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Activate(game);NextAttack(game,"brogan-02-投掷飞斧");Apply(game,2,CommandKind.BeginPrimary);Apply(game,2,CommandKind.ChooseOptionalDiscard,"skip");Apply(game,2,CommandKind.ChooseAttackTarget,"hero:1");Assert.That(game.View(2).Attack!.EnemySupportSources,Is.Empty);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void SourceOwnBasicAttackAlsoUsesTheAura()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Activate(game);Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(3,-8));Apply(game,0,CommandKind.DebugTeleport,Heavy,cell:new Hex(4,-7));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(5,-9));NextAttack(game,sabina:"sabina-00-近身射击",actor:0);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(0).Attack!.EnemySupportSources,Is.EquivalentTo(new[]{Melee,Heavy}));Assert.That(game.View(0).Attack!.FinalAttack,Is.EqualTo(4));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void EnemyAttackDoesNotConvertDefendersMeleeGuard()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Activate(game);Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(5,-7));Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(5,-8));NextAttack(game,wasp:"wasp-00-闪耀之刃",actor:3);Apply(game,3,CommandKind.BeginPrimary);Apply(game,3,CommandKind.ChooseAttackTarget,"hero:2");Assert.That(game.View(3).Attack!.FriendlyGuardSources,Does.Contain(Melee));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void AuraFollowsSourcePositionAndRadiusBonus()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Activate(game,true);NextAttack(game);var codec=new JsonStateCodec();var original=codec.Read(game.ExportSave());
            foreach(int bonus in new[]{0,1})
            {
                var state=codec.Read(codec.Write(original));state.Players[0].RangeBonus=bonus;var rules=new GameRules();rules.Apply(catalog,state,new Command{ActorSeat=2,Kind=CommandKind.BeginPrimary});rules.Apply(catalog,state,new Command{ActorSeat=2,Kind=CommandKind.ChooseAttackTarget,Value="hero:1"});Assert.That(state.Execution!.Attack!.EnemySupportSources,Is.EquivalentTo(bonus==0?new[]{Melee}:new[]{Melee,Heavy}));
            }
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(1,-8));Apply(game,2,CommandKind.BeginPrimary);Apply(game,2,CommandKind.ChooseAttackTarget,"hero:1");Assert.That(game.View(2).Attack!.EnemySupportSources,Is.Empty);
        }
        [Test]
        public void NearbyMinionContributesOnceRatherThanMeleePlusRanged()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Activate(game);Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(6,-7));Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(5,-7));NextAttack(game);Apply(game,2,CommandKind.BeginPrimary);Apply(game,2,CommandKind.ChooseAttackTarget,"hero:1");Assert.That(game.View(2).Attack!.EnemySupportSources.Count(id=>id==Melee),Is.EqualTo(1));Assert.That(game.View(2).Attack!.EnemySupportSources.Distinct().Count(),Is.EqualTo(game.View(2).Attack!.EnemySupportSources.Count));
        }
        [Test]
        public void RetrievingTheResolvedMovementCardCancelsItsAura()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Activate(game);Apply(game,0,CommandKind.DebugTeleport,Melee,cell:new Hex(4,-7));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(5,-8));NextAttack(game,"brogan-10-吟游诗人");Apply(game,2,CommandKind.BeginPrimary);Apply(game,2,CommandKind.ChooseEffectTarget,"hero:0");Apply(game,0,CommandKind.ChooseRecoveredCard,Drill);Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Drill),Is.False);Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Drill).Zone,Is.EqualTo(CardZone.InHand));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void DefeatingSourceCancelsAuraBeforeRespawn()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Activate(game);Apply(game,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Assert.That(game.View(0).Effects,Is.Empty);Assert.That(game.View(0).Events.Any(e=>e.Kind=="EffectCancelled" && e.CardId==Drill),Is.True);game=ChargeTests.Restore(catalog,game);
            NextAttack(game,sabina:"sabina-00-近身射击",actor:0);Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("hero_respawn"));Apply(game,0,CommandKind.RespawnHero,cell:game.View(0).RespawnCells.First());Assert.That(game.View(0).Effects,Is.Empty);Assert.That(game.View(0).CanBeginPrimary,Is.True);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void EffectExpiresAtRoundBoundaryRatherThanNextTurn()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Activate(game);Apply(game,0,CommandKind.DebugAdvance,"turn");Assert.That(game.View(0).Turn,Is.EqualTo(2));Assert.That(game.View(0).Effects.Count,Is.EqualTo(1));
            for(int turn=2;turn<=4;turn++){Apply(game,0,CommandKind.DebugSelectAll,"first");Apply(game,0,CommandKind.DebugAdvance,"turn");}
            Assert.That(game.View(0).Effects,Is.Empty);Assert.That(game.View(0).Events.Any(e=>e.Kind=="EffectExpired" && e.CardId==Drill),Is.True);ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void CancellingAnotherSourceKeepsDrillAndHidesPrivateFutureOrigins()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,"static");Apply(game,3,CommandKind.BeginPrimary);Activate(game);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());
            // Isolate the cancellation lifetime/privacy rule with a future private effect owned by the existing Wasp.
            state.Effects.Add(new ActiveEffect{Id="future-private",SourceCardId="wasp-10-反射屏障",SourceUnitId="hero:3",ControllerSeat=3,SourcePrivateTo=3,Kind=EffectKind.NonAdjacentRangedImmunity,Window=EffectTimeline.Create(1,1,4,EffectDuration.NextTurn)!});
            game=new GameSession(catalog,codec,state);Apply(game,0,CommandKind.DebugDefeatHero,"hero:3",target:0);Assert.That(game.View(0).Effects.Select(e=>e.SourceCardId),Is.EqualTo(new[]{Drill}));
            Assert.That(game.View(3).Events.Any(e=>e.Kind=="EffectCancelled" && e.CardId=="wasp-10-反射屏障"),Is.True);Assert.That(game.View(0).Events.Any(e=>e.CardId=="wasp-10-反射屏障"),Is.False);Assert.That(game.View(null).Events.Any(e=>e.Kind=="ProtectionExpired"),Is.True);
        }
        [TestCase("static")] [TestCase("silence")]
        public void OrdinaryMovementRespectsStaticButIsNotASkill(string interference)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,interference);Apply(game,3,CommandKind.BeginPrimary);Assert.That(game.View(0).CanBeginPrimary,Is.True);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.Any(m=>m.Destination==new Hex(5,-7)),Is.EqualTo(interference=="silence"));Apply(game,0,CommandKind.ChooseEffectMove,"skip");Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Drill),Is.True);ChargeTests.Restore(catalog,game);
        }
        [TestCase(29)] [TestCase(30)]
        public void DefeatCancellationIsVersionedForExistingAuras(int engine)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,"static",engine);Apply(game,3,CommandKind.BeginPrimary);Apply(game,0,CommandKind.DebugDefeatHero,"hero:3",target:0);
            Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId=="wasp-06-静电封锁"),Is.EqualTo(engine<30));ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void FrozenBlinkDefenseRetainsExactBytesAndHasNoMovementProgram()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine29-blink-defense.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Drill));Apply(game,1,CommandKind.Defend,"sabina-01-拔枪");ChargeTests.Restore(catalog,game);
        }
    }
}

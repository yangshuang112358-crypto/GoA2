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
    public sealed class PhantasmTests
    {
        internal const string Card = "shargatha-12-幻化", Slash = "shargatha-01-劈砍";
        internal static GameSession Setup(ContentCatalog catalog, bool owned = true, int engine = GameState.CurrentEngineVersion,
            string played = Slash, bool advance = true)
        {
            var game = LocalGameFactory.Create(catalog, "phantasm", new[] { "A", "B", "C", "D" }, 42, true, engine);
            Apply(game, 0, CommandKind.DebugPrepare, "shargatha,sabina,brogan,arien");
            if (owned)
            {
                Apply(game, 0, CommandKind.DebugSetGold, "28", target: 0);
                Apply(game, 0, CommandKind.DebugAdvance, "round"); Apply(game, 0, CommandKind.ResolveRoundEnd);
                for (int i = 0; i < 7; i++) Apply(game, 0, CommandKind.ChooseUpgrade, game.View(0).UpgradeOptions.First().CardId);
            }
            Apply(game, 0, CommandKind.DebugEquipCard, played == "shargatha-06-海妖之歌" ? Slash : played, target: 0);
            for (int i = 0; i < 4; i++) Apply(game, 0, CommandKind.DebugTeleport, "hero:" + i,
                cell: new[] { new Hex(6,-8), new Hex(7,-8), new Hex(4,-8), new Hex(6,-5) }[i]);
            string[] cards = { played, "sabina-00-近身射击", "brogan-06-铜墙铁壁", "arien-07-潮水" };
            for (int i = 0; i < 4; i++) Apply(game, i, CommandKind.SelectCard, cards[i]);
            if (advance) OpportuneMomentTests.AdvanceTo(game, 0);
            return game;
        }

        private static void ChooseAndPay(GameSession game)
        {
            Assert.That(game.View(0).Pending?.Source, Is.EqualTo(Card));
            Assert.That(game.View(0).EffectTargets, Does.Contain("hero:1"));
            Apply(game, 0, CommandKind.ChooseEffectTarget, "hero:1");
            Apply(game, 1, CommandKind.ForcedDiscard, game.View(1).ForcedDiscardCards.First());
        }

        [Test]
        public void PrimaryPausesBeforeAttackAndRestoresBothChoiceWindows()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            Apply(game, 0, CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Source, Is.EqualTo(Card));
            Assert.That(game.View(0).Events.Any(e => e.Kind == "AttackDeclared"), Is.False);
            game = ChargeTests.Restore(catalog, game);
            Apply(game, 0, CommandKind.ChooseEffectTarget, "hero:1");
            game = ChargeTests.Restore(catalog, game);
            Apply(game, 1, CommandKind.ForcedDiscard, game.View(1).ForcedDiscardCards.First());
            Assert.That(game.View(0).Pending?.Kind, Is.EqualTo("attack_target"));
            Assert.That(game.View(0).Pending?.Source, Is.EqualTo(Slash));
            Assert.That(game.View(0).Events.Count(e => e.Kind == "UltimateTriggered" && e.CardId == Card), Is.EqualTo(1));
            Apply(game, 0, CommandKind.ChooseAttackTarget, "hero:1"); Apply(game, 1, CommandKind.DeclineDefense);
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void SecondaryMoveWaitsForDiscardAndOnlyMovesOnce()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            var destination = game.View(0).SecondaryMoves.First().Destination;
            Apply(game, 0, CommandKind.Move, cell: destination);
            Assert.That(game.View(0).Units.Single(u => u.Seat == 0).Position, Is.EqualTo(new Hex(6,-8)));
            ChooseAndPay(game);
            Assert.That(game.View(0).Units.Single(u => u.Seat == 0).Position, Is.EqualTo(destination));
            Assert.That(game.View(0).Events.Count(e => e.Kind == "UnitMoved" && e.Seat == 0), Is.EqualTo(1));
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void DefensePreludePreservesOriginalAttackAndDoesNotSpendDefenseEarly()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog, played: "shargatha-06-海妖之歌", advance: false);
            OpportuneMomentTests.AdvanceTo(game, 1);
            Apply(game, 1, CommandKind.BeginPrimary); Apply(game, 1, CommandKind.ChooseAttackTarget, "hero:0");
            Apply(game, 0, CommandKind.Defend, Slash);
            Assert.That(game.View(0).Pending?.Source, Is.EqualTo(Card));
            Assert.That(game.View(null).Attack, Is.Not.Null, "公开攻击数值在防御前插入窗口中仍应可见");
            Assert.That(game.View(0).OwnCards.Single(c => c.CardId == Slash).Zone, Is.EqualTo(CardZone.InHand));
            game = ChargeTests.Restore(catalog, game); ChooseAndPay(game);
            Assert.That(game.View(0).OwnCards.Single(c => c.CardId == Slash).Zone, Is.EqualTo(CardZone.Discarded));
            Assert.That(game.View(0).Events.Count(e => e.Kind == "AttackResolved"), Is.EqualTo(1));
            Assert.That(game.View(0).Events.Count(e => e.Kind == "UltimateTriggered" && e.CardId == Card), Is.EqualTo(1));
            ChargeTests.Restore(catalog, game);
        }

        [Test]
        public void WallsCanBeCrossedButNeverUsedAsDestinations()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            var occupied = game.View(0).Units.Select(u => u.Position).ToList();
            var pair = catalog.Cells.Where(c => !c.Obstacle && !occupied.Contains(c.Position))
                .SelectMany(c => c.Position.Neighbors().Select(w => new { Origin = c.Position, Wall = w, End = new Hex(2*w.X-c.Position.X,2*w.Y-c.Position.Y) }))
                .First(p => catalog.Cell(p.Wall)?.Obstacle == true && catalog.Cell(p.End)?.Obstacle == false && !occupied.Contains(p.End));
            Apply(game, 0, CommandKind.DebugTeleport, "hero:0", cell: pair.Origin);
            var moves = game.View(0).SecondaryMoves;
            Assert.That(moves.Single(m => m.Destination == pair.End).Path.Count, Is.EqualTo(3));
            Assert.That(moves.Any(m => catalog.Cell(m.Destination).Obstacle), Is.False);
        }

        [TestCase(false, 56)] [TestCase(true, 56)] [TestCase(false, GameState.CurrentEngineVersion)]
        public void OldEngineAndUnownedAbilityDoNotInterceptActions(bool owned, int engine)
        {
            var game = Setup(BattlefieldTests.Catalog(), owned, engine);
            Apply(game, 0, CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Kind, Is.EqualTo("attack_target"));
        }

        [Test]
        public void EmptyAdjacentEnemyMayBeChosenDespiteAnotherEnemyHavingCards()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            Apply(game, 0, CommandKind.DebugTeleport, "hero:3", cell: new Hex(6,-7));
            foreach (var c in game.View(1).OwnCards.Where(c => c.Zone == CardZone.InHand).ToList())
                Apply(game, 0, CommandKind.DebugDiscard, c.CardId, target: 1);
            Apply(game, 0, CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectTargets, Is.EquivalentTo(new[] { "hero:1", "hero:3" }));
            Apply(game, 0, CommandKind.ChooseEffectTarget, "hero:1");
            Assert.That(game.View(0).Pending?.Kind, Is.EqualTo("attack_target"));
            Assert.That(game.View(0).Events.Count(e => e.Kind == "ForcedDiscardSkipped"), Is.EqualTo(1));
            Assert.That(game.View(0).Events.Any(e => e.Kind == "ForcedDiscardRequired"), Is.False);
            ChargeTests.Restore(catalog, game);
        }

        [TestCase(false)] [TestCase(true)]
        public void InvalidCommandsAreAtomicInBothPreludeWindows(bool payment)
        {
            var game = Setup(BattlefieldTests.Catalog()); Apply(game, 0, CommandKind.BeginPrimary);
            if (payment) Apply(game, 0, CommandKind.ChooseEffectTarget, "hero:1");
            string before = game.ExportSave();
            foreach (var cmd in new[] { Cmd(game,0,CommandKind.BeginPrimary), Cmd(game,0,CommandKind.Pass),
                Cmd(game,0,CommandKind.DebugTeleport,"hero:0",destination:new Hex(5,-8)),
                Cmd(game,0,CommandKind.ChooseEffectTarget,"hero:2"), Cmd(game,0,CommandKind.ChooseEffectTarget,"skip"),
                Cmd(game,1,CommandKind.DeclineRetaliationDiscard), Cmd(game,0,CommandKind.ForcedDiscard,Slash),
                Cmd(game,1,CommandKind.ForcedDiscard,"sabina-00-近身射击") })
            {
                Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted, Is.False, cmd.Kind.ToString());
                Assert.That(game.ExportSave(), Is.EqualTo(before));
            }
        }

        [Test]
        public void PaymentIsPrivateAndDuplicateCompletionCannotResumeTwice()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Setup(catalog);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseEffectTarget,"hero:1");
            string card = game.View(1).ForcedDiscardCards.First(); var command = Cmd(game,1,CommandKind.ForcedDiscard,card);
            Assert.That(game.Execute(1,command).Accepted, Is.True); string after = game.ExportSave();
            Assert.That(game.Execute(1,command).Duplicate, Is.True); Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.View(null).Events.Any(e=>e.Kind=="CardDiscarded" && e.CardId==card),Is.False);
            Assert.That(game.View(1).Events.Any(e=>e.Kind=="CardDiscarded" && e.CardId==card),Is.True);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="PrimaryActionStarted" && e.CardId==Slash),Is.EqualTo(1));
            ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void PassingAndIllegalMovementDoNotTriggerAndNoTargetDoesNotBlock()
        {
            var game = Setup(BattlefieldTests.Catalog()); string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.Move,destination:new Hex(7,-8))).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before)); Apply(game,0,CommandKind.Pass);
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="UltimateTriggered"),Is.False);
            game=Setup(BattlefieldTests.Catalog()); Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Source,Is.Not.EqualTo(Card));
            Assert.That(game.View(0).Events.Last(e=>e.Kind=="UltimateCompleted").Detail,Is.EqualTo("no_targets"));
        }

        [Test]
        public void FastMoveAcrossRegionBoundaryAlsoWaitsForAdjacentDiscard()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog);
            // Find a legal boundary placement while keeping every other enemy out of the origin region.
            var state=new JsonStateCodec().Read(game.ExportSave()); var mage=state.Units.Single(u=>u.Seat==0);
            var enemy=state.Units.Single(u=>u.Seat==1); Hex origin=default, adjacent=default, destination=default; bool found=false;
            foreach(var cell in catalog.Cells.Where(c=>!c.Obstacle && !state.Units.Any(u=>u.Position==c.Position)))
            {
                foreach(var next in cell.Position.Neighbors().Where(p=>catalog.Cell(p)?.Obstacle==false && catalog.Cell(p).Region!=cell.Region && !state.Units.Any(u=>u.Position==p)))
                {
                    mage.Position=cell.Position; enemy.Position=next; var moves=MovementRules.LegalMoves(catalog,state,0,MoveMode.Fast);
                    if(moves.Count==0)continue; origin=cell.Position; adjacent=next; destination=moves[0].Destination; found=true; break;
                }
                if(found)break;
            }
            Assert.That(found,Is.True); Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:origin);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:adjacent);
            var command=Cmd(game,0,CommandKind.Move,destination:destination); command.MoveMode=MoveMode.Fast;
            Assert.That(game.Execute(0,command).Accepted,Is.True); ChooseAndPay(game);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(destination));
            ChargeTests.Restore(catalog,game);
        }

        [TestCase(false)] [TestCase(true)]
        public void RepeatedAttackGetsItsOwnPreludeButSkippingRepeatDoesNot(bool skip)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog,played:PikeTests.Pike);
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(6,-6));
            Apply(game,0,CommandKind.BeginPrimary); ChooseAndPay(game);
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:3"); Apply(game,3,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Pending?.ResumeAt,Is.EqualTo("repeat_once_different"));
            Apply(game,0,CommandKind.ChooseAttackTarget,skip?"skip":"minion:-1,-3");
            if(!skip){game=ChargeTests.Restore(catalog,game);ChooseAndPay(game);}
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UltimateTriggered" && e.CardId==Card),Is.EqualTo(skip?1:2));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(skip?1:2));
            ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void ImmunityAndSourceSpecificImmunityFilterPreludeTargets()
        {
            // Isolated legality snapshot, not a replay fixture.
            var catalog=BattlefieldTests.Catalog(); var game=Setup(catalog); Apply(game,0,CommandKind.BeginPrimary);
            var state=new JsonStateCodec().Read(game.ExportSave());
            var effect=new ActiveEffect {SourceUnitId="hero:1",ProtectedUnitId="hero:1",Kind=EffectKind.ImmunityAndUnitTraversal,
                Window=new EffectWindow {StartRound=state.Round,EndRound=state.Round,StartTurn=state.Turn,EndTurn=state.Turn}};
            state.Effects.Add(effect); Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Is.Empty);
            effect.Kind=EffectKind.OtherEnemyActionImmunity; effect.ExemptControllerSeat=2;
            Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Is.Empty);
            effect.ExemptControllerSeat=0; Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Does.Contain("hero:1"));
        }

        [TestCase(1)] [TestCase(2)]
        public void TraversesEnemyOrFriendlyUnitButCannotStopOnIt(int middle)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            Apply(game,0,CommandKind.DebugTeleport,"hero:"+middle,cell:new Hex(6,-7));
            var moves=game.View(0).SecondaryMoves;
            Assert.That(moves.Single(m=>m.Destination==new Hex(6,-6)).Path.Count,Is.EqualTo(3));
            Assert.That(moves.Any(m=>m.Destination==new Hex(6,-7)),Is.False);
        }

        [Test]
        public void BindingIsExactAndPreviousHeavyWeaponrySaveRemainsByteStable()
        {
            var catalog=BattlefieldTests.Catalog(); Assert.That(UltimateRules.HasProgram(catalog.Card(Card)),Is.True);
            Assert.That(UltimateRules.HasProgram(catalog.Card(Card),56),Is.False);
            catalog.Card(Card).Text+="可选";Assert.That(UltimateRules.HasProgram(catalog.Card(Card)),Is.False);
            catalog=BattlefieldTests.Catalog();string save=System.IO.File.ReadAllText(System.IO.Path.Combine(ContentTests.Root(),"tests/fixtures/engine56-heavy-weaponry-payment.json"));
            var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));
            Assert.That(game.View(0).SupportedUltimateCards,Does.Not.Contain(Card));
            Apply(game,1,CommandKind.ForcedDiscard,"tigerclaw-00-瞬闪打击");ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void OrdinarySkillPreludeCompletesBeforeRecoveryChoice()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,played:LoyalFollowerTests.Loyal);
            Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(6,-7));
            Apply(game,0,CommandKind.DebugDiscard,"shargatha-00-反击",target:0);
            Apply(game,0,CommandKind.BeginPrimary);ChooseAndPay(game);
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("recover_discard"));
            Apply(game,0,CommandKind.ChooseRecoveredCard,"shargatha-00-反击");
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UltimateTriggered" && e.CardId==Card),Is.EqualTo(1));
            ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void TraversalDoesNotBypassStaticBoundaryOrTurnPushIntoMovement()
        {
            // Isolated geometry policy: Phantasm changes traversal, not movement prohibitions or push.
            var catalog=BattlefieldTests.Catalog();var state=new JsonStateCodec().Read(Setup(catalog).ExportSave());
            var mage=state.Units.Single(u=>u.Seat==0);var enemy=state.Units.Single(u=>u.Seat==1);
            state.Effects.Add(new ActiveEffect {SourceCardId="wasp-06-静电封锁",SourceUnitId=enemy.Id,ControllerSeat=1,
                Kind=EffectKind.MovementBoundary,AreaKind=EffectAreaKind.Adjacent,
                Window=new EffectWindow {StartRound=state.Round,EndRound=state.Round,StartTurn=state.Turn,EndTurn=state.Turn}});
            Assert.That(UltimateRules.CanTraverseObstacles(catalog,state,mage),Is.True);
            Assert.That(MovementRules.LegalMoves(catalog,state,0,MoveMode.Secondary).All(m=>m.Destination.Distance(enemy.Position)<=1),Is.True);
            state.Effects.Clear();state.Units.Single(u=>u.Seat==2).Position=new Hex(5,-8);
            var push=PushRules.AwayFromAdjacent(catalog,state,enemy,mage,2);
            Assert.That(push.Path.Count,Is.EqualTo(1));Assert.That(push.StopReason,Is.EqualTo("occupied"));
        }

        [Test]
        public void StraightMovementTraversesWallAtExactBudgetWithoutCreatingAnAction()
        {
            var catalog=BattlefieldTests.Catalog();var state=new JsonStateCodec().Read(Setup(catalog).ExportSave());
            var mage=state.Units.Single(u=>u.Seat==0);var occupied=state.Units.Select(u=>u.Position).ToList();
            var pair=catalog.Cells.Where(c=>!c.Obstacle && !occupied.Contains(c.Position))
                .SelectMany(c=>c.Position.Neighbors().Select(w=>new {Origin=c.Position,Wall=w,End=new Hex(2*w.X-c.Position.X,2*w.Y-c.Position.Y)}))
                .First(p=>catalog.Cell(p.Wall)?.Obstacle==true && catalog.Cell(p.End)?.Obstacle==false && !occupied.Contains(p.End));
            mage.Position=pair.Origin;
            var method=typeof(MovementRules).GetMethod("StraightExact",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            var moves=(System.Collections.Generic.List<MoveOption>)method.Invoke(null,new object[]{catalog,state,mage,2,null});
            Assert.That(moves.Single(m=>m.Destination==pair.End).Path,Does.Contain(pair.Wall));
            var shortMoves=(System.Collections.Generic.List<MoveOption>)method.Invoke(null,new object[]{catalog,state,mage,1,null});
            Assert.That(shortMoves.Any(m=>m.Destination==pair.Wall || m.Destination==pair.End),Is.False);
            Assert.That(state.Events.Any(e=>e.Kind=="UltimateTriggered"),Is.False);
        }
    }
}

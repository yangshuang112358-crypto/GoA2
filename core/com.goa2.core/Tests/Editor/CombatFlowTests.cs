using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class CombatFlowTests
    {
        internal static GameSession Duel(ContentCatalog catalog, string attacker="sabina", string attackCard="sabina-01-拔枪", string defender="wasp", string? equipment=null, int engineVersion=GameState.CurrentEngineVersion)
        {
            var game = LocalGameFactory.Create(catalog,"combat",new[] { "A","B","C","D" },42,true,engineVersion);
            Apply(game,0,CommandKind.DebugPrepare,attacker+","+defender+",brogan,"+(defender == "arien" ? "wasp" : "arien"));
            if (!game.View(0).OwnCards.Any(c => c.CardId == attackCard)) Apply(game,0,CommandKind.DebugEquipCard,attackCard,target:0);
            if (equipment != null) Apply(game,0,CommandKind.DebugEquipCard,equipment,target:1);
            var empty = catalog.Cells.Where(c => c.Region == "redFountain" && !c.Obstacle && !game.View(null).Units.Any(u => u.Position == c.Position)).OrderBy(c => c.Position.X).ThenBy(c => c.Position.Y).ToList();
            var source = empty.First(a => empty.Any(b => a.Position.Distance(b.Position) == 2));
            var target = empty.First(b => source.Position.Distance(b.Position) == 2);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:source.Position);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:target.Position);
            for (int seat=0;seat<4;seat++)
            {
                string selected = seat == 0 ? attackCard : game.View(seat).OwnCards.Select(c => catalog.Card(c.CardId))
                    .Where(c => c.PrimaryFamily != "defense" && c.Color != "gold").OrderBy(c => c.Initiative).ThenBy(c => c.Id,System.StringComparer.Ordinal).First().Id;
                Apply(game,seat,CommandKind.SelectCard,selected);
            }
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(0));
            return game;
        }
        private static void Attack(GameSession game, string target="hero:1")
        {
            Apply(game,0,CommandKind.BeginPrimary);
            Apply(game,0,CommandKind.ChooseAttackTarget,target);
        }
        [TestCase("sabina","sabina-01-拔枪")]
        [TestCase("shargatha","shargatha-02-快速突刺")]
        public void PrimaryActionLocksOtherActionsAndBothChoicesRestore(string hero, string card)
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog,hero,card);
            Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("attack_target"));
            Assert.That(game.View(0).CanPass, Is.False); Assert.That(game.View(0).SecondaryMoves, Is.Empty);
            string before = game.ExportSave();
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.ChooseAttackTarget,"hero:1")).Accepted, Is.False);
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:2")).Accepted, Is.False);
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.Pass)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            game = LocalGameFactory.Restore(catalog,before);
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("defense"));
            Assert.That(game.View(null).Pending!.ChooserSeat, Is.EqualTo(1));
            Assert.That(game.View(null).Attack!.FinalAttack, Is.EqualTo(4));
            Assert.That(game.View(0).DefenseOptions, Is.Empty); Assert.That(game.View(null).DefenseOptions, Is.Empty);
            Assert.That(game.View(1).DefenseOptions, Is.Not.Empty);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void FailedDefensePaysRewardsAndRespawnContinuesTheVictimsNextCard()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog); Attack(game);
            string victimPlayed = game.View(null).Players[1].Revealed.Single().CardId;
            string before = game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.Defend,"wasp-00-闪耀之刃")).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            var defend = Cmd(game,1,CommandKind.Defend,"wasp-00-闪耀之刃");
            Assert.That(game.Execute(1,defend).Accepted, Is.True); Assert.That(game.Execute(1,defend).Duplicate, Is.True);
            var view = game.View(null);
            Assert.That(view.Players[1].AwaitingRespawn, Is.True); Assert.That(view.Units.Any(u => u.Seat == 1), Is.False);
            Assert.That(view.Players[0].Gold, Is.EqualTo(1)); Assert.That(view.Players[2].Gold, Is.EqualTo(1)); Assert.That(view.RedCrystal, Is.EqualTo(6));
            for (int n=0;n<5 && game.View(null).Pending?.Kind != "hero_respawn";n++)
            {
                view = game.View(null);
                if (view.Pending?.Kind == "initiative") Apply(game,view.Pending.ChooserSeat,CommandKind.ChooseInitiative,target:view.Pending.CandidateSeats.Contains(1)?1:view.Pending.CandidateSeats[0]);
                else Apply(game,view.ActiveSeat!.Value,CommandKind.Pass);
            }
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("hero_respawn"));
            game = LocalGameFactory.Restore(catalog,game.ExportSave());
            var cell = game.View(1).RespawnCells.First();
            before = game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.RespawnHero,destination:cell)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game,1,CommandKind.RespawnHero,cell:cell);
            view = game.View(null);
            Assert.That(view.Phase, Is.EqualTo(Phase.Action)); Assert.That(view.ActiveSeat, Is.EqualTo(1));
            Assert.That(view.Players[1].AwaitingRespawn, Is.False); Assert.That(view.RedCrystal, Is.EqualTo(6));
            Assert.That(view.Units.Single(u => u.Seat == 1).Position, Is.EqualTo(cell));
            Assert.That(view.Players[1].Revealed.Single(c => c.Zone == CardZone.PlayedUnresolved).CardId, Is.EqualTo(victimPlayed));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase("wasp","wasp-07-抵挡屏障","green")]
        [TestCase("tigerclaw","tigerclaw-18-躲闪","blue")]
        public void PrimaryBlockDiscardsPrivatelyWithoutBorrowingHigherCardEffects(string defender, string response, string color)
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog,defender:defender); Attack(game);
            Apply(game,1,CommandKind.Defend,response);
            var view = game.View(null);
            Assert.That(view.Units.Any(u => u.Seat == 1), Is.True); Assert.That(view.RedCrystal, Is.EqualTo(7));
            Assert.That(view.Players[1].DiscardColors, Is.EqualTo(new[] { color }));
            Assert.That(view.Events.Any(e => e.CardId == response), Is.False);
            Assert.That(game.View(1).Events.Any(e => e.Kind == "CardDiscarded" && e.CardId == response), Is.True);
            Assert.That(view.Players[0].DiscardColors, Is.Empty);
            Assert.That(view.Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(2,0)]
        [TestCase(0,2)]
        public void ChallengerResponseIgnoresBothSignsWithoutMovingOrDeletingMinions(int enemies, int friends)
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog,defender:"arien");
            var target = game.View(null).Units.Single(u => u.Seat == 1).Position;
            var cells = catalog.Cells.Where(c => !c.Obstacle && c.Position.Distance(target) == 1 && !game.View(null).Units.Any(u => u.Position == c.Position)).Select(c => c.Position).ToList();
            var minions = game.View(null).Units.Where(u => u.Kind == "melee" && u.Team == Team.Blue).Take(enemies)
                .Concat(game.View(null).Units.Where(u => u.Kind == "melee" && u.Team == Team.Red).Take(friends)).ToList();
            for (int i=0;i<minions.Count;i++) Apply(game,0,CommandKind.DebugTeleport,minions[i].Id,cell:cells[i]);
            Attack(game);
            var attack = game.View(null).Attack!;
            Assert.That(attack.EnemySupport, Is.EqualTo(enemies)); Assert.That(attack.FriendlyGuard, Is.EqualTo(friends));
            var option = game.View(1).DefenseOptions.Single(o => o.CardId == "arien-13-挑战者");
            Assert.That(option.Assessment.AttackCompared, Is.EqualTo(4)); Assert.That(option.Assessment.Successful, Is.True);
            game = LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,1,CommandKind.Defend,"arien-13-挑战者");
            Assert.That(game.View(null).Units.Any(u => u.Seat == 1), Is.True);
            Assert.That(game.View(null).Players[1].DiscardColors, Is.EqualTo(new[] { "blue" }));
            for (int i=0;i<minions.Count;i++) Assert.That(game.View(null).Units.Single(u => u.Id == minions[i].Id).Position, Is.EqualTo(cells[i]));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void EmptyDefenseHandDefeatsWithoutRequestingAFakeCard()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog);
            foreach (var card in game.View(1).OwnCards.Where(c => c.Zone == CardZone.InHand).ToList()) Apply(game,0,CommandKind.DebugDiscard,card.CardId,target:1);
            Attack(game);
            Assert.That(game.View(null).Units.Any(u => u.Seat == 1), Is.False);
            Assert.That(game.View(null).Pending?.Kind, Is.Not.EqualTo("defense"));
            Assert.That(game.View(null).Events.Any(e => e.Kind == "NoDefenseAvailable"), Is.True);
        }
        [Test]
        public void UnimplementedPrimaryResponseWaitsRatherThanPretendingTheHandIsEmpty()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog,equipment:"wasp-08-偏转屏障",engineVersion:3);
            foreach (var card in game.View(1).OwnCards.Where(c => c.Zone == CardZone.InHand && c.CardId != "wasp-08-偏转屏障").ToList()) Apply(game,0,CommandKind.DebugDiscard,card.CardId,target:1);
            Attack(game);
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("defense"));
            Assert.That(game.View(1).DefenseOptions, Is.Empty); Assert.That(game.View(1).UnimplementedDefenseCards, Is.EqualTo(new[] { "wasp-08-偏转屏障" }));
            string before = game.ExportSave();
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.Defend,"wasp-08-偏转屏障")).Code, Is.EqualTo("response_not_implemented"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(null).Players[1].AwaitingRespawn, Is.True);
        }
        [Test]
        public void AttackWithNoLegalTargetsEndsTheCommittedCard()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog);
            var source = game.View(null).Units.Single(u => u.Seat == 0).Position;
            var adjacent = catalog.Cells.First(c => !c.Obstacle && c.Position.Distance(source) == 1 && !game.View(null).Units.Any(u => u.Position == c.Position));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:adjacent.Position);
            Assert.That(game.View(0).AttackTargets, Is.Empty);
            Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(null).Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(null).Events.Any(e => e.Kind == "CardEffectStopped" && e.Detail == "no_targets"), Is.True);
        }
        [TestCase("melee",2)]
        [TestCase("ranged",2)]
        [TestCase("heavy",4)]
        public void MinionAttackPaysTheCorrectRewardWithoutAHeroDefenseWindow(string kind, int reward)
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog);
            var target = game.View(null).Units.First(u => u.Team == Team.Red && u.Kind == kind);
            if (kind == "heavy")
                foreach (var other in game.View(null).Units.Where(u => u.Team == Team.Red && u.Kind != "hero" && u.Kind != "heavy").ToList()) Apply(game,0,CommandKind.DebugRemoveMinion,other.Id);
            var source = catalog.Cells.First(c => !c.Obstacle && c.Position.Distance(target.Position) == 2 && !game.View(null).Units.Any(u => u.Position == c.Position));
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:source.Position);
            Attack(game,target.Id);
            Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(reward));
            Assert.That(game.View(null).Units.Any(u => u.Id == target.Id), Is.False);
            Assert.That(game.View(null).Events.Any(e => e.Kind == "DefenseChoiceRequired"), Is.False);
            Assert.That(game.View(null).Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void HeavyAttackResumesItsExactCardStepAfterCaptainSpawnAndRestore()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog);
            foreach (var other in game.View(null).Units.Where(u => u.Team == Team.Red && u.Kind != "hero" && u.Kind != "heavy").ToList()) Apply(game,0,CommandKind.DebugRemoveMinion,other.Id);
            var target = game.View(null).Units.Single(u => u.Team == Team.Red && u.Kind == "heavy");
            var source = catalog.Cells.First(c => !c.Obstacle && c.Position.Distance(target.Position) == 2 && !game.View(null).Units.Any(u => u.Position == c.Position));
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:source.Position);
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(2,-7));
            Attack(game,target.Id);
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("minion_spawn"));
            Assert.That(game.View(null).Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedUnresolved));
            Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(4));
            game = LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,1,CommandKind.ChooseMinionSpawn,cell:new Hex(1,-7));
            Assert.That(game.View(null).Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(null).Events.Count(e => e.Kind == "AttackResolved"), Is.EqualTo(1));
            Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(4));
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution, Is.Null);
        }
        [Test]
        public void FullyOccupiedHeroSpawnsRemainPendingUntilAnActualSpawnBecomesFree()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog);
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(8,-6));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(7,-7));
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));
            Attack(game); Apply(game,1,CommandKind.Defend,"wasp-00-闪耀之刃");
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("hero_respawn")); Assert.That(game.View(1).RespawnCells, Is.Empty);
            game = LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Assert.That(game.View(1).RespawnCells, Is.EqualTo(new[] { new Hex(7,-9) }));
            Apply(game,1,CommandKind.RespawnHero,cell:new Hex(7,-9));
            Assert.That(game.View(null).RedCrystal, Is.EqualTo(6));
        }
        [Test]
        public void StructuralDebugToolsCannotAlterAnOutstandingDefense()
        {
            var catalog = BattlefieldTests.Catalog(); var game = Duel(catalog); Attack(game);
            Assert.That(game.View(0).DebugTeleports.Values.All(cells => cells.Count == 0), Is.True);
            string before = game.ExportSave();
            var minion = game.View(null).Units.First(u => u.Kind == "melee");
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DebugRemoveMinion,minion.Id)).Code, Is.EqualTo("pending_card"));
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DebugDiscard,"wasp-00-闪耀之刃",target:1)).Code, Is.EqualTo("pending_card"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }
    }
}

using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class RoundEndTests
    {
        [Test]
        public void RoundBattleVictoryStopsBeforeChargingUpgradesOrGrantingCompensation()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugRemoveMinion,BattlefieldTests.Heavy(game,Team.Red));
            foreach(var unit in game.View(null).Units.Where(u => u.Team==Team.Red && u.Kind!="heavy" && u.Kind!="hero").ToList()) Apply(game,0,CommandKind.DebugRemoveMinion,unit.Id);
            Apply(game,0,CommandKind.DebugSetGold,"6",target:0); Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Apply(game,1,CommandKind.ChooseRoundMinionRemoval,BattlefieldTests.Heavy(game,Team.Red));
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.Finished)); Assert.That(game.View(null).Winner, Is.EqualTo(Team.Blue));
            Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(6)); Assert.That(game.View(null).Players.All(p => p.Level==1), Is.True);
            Assert.That(game.View(null).Events.Any(e => e.Kind=="UpgradesStarted" || e.Kind=="RoundCompensationGranted"), Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        internal static void FinishUpgrades(GameSession game)
        {
            for (int step=0;step<24;step++)
            {
                var seats = game.View(null).UpgradingSeats;
                if (seats.Count == 0) return;
                int seat=seats[0];
                var options = game.View(seat).UpgradeOptions;
                Assert.That(options, Is.Not.Empty, "An upgrade must expose an actual legal option");
                Apply(game,seat,CommandKind.ChooseUpgrade,options[0].CardId);
            }
            Assert.Fail("Upgrade loop did not finish");
        }
        [Test]
        public void EqualMinionsRecallEveryCardAndCompensateWithoutSpendingThatCompensation()
        {
            var catalog = BattlefieldTests.Catalog(); var game = BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugDiscard,"brogan-00-猛攻",target:2);
            Apply(game,0,CommandKind.DebugAdvance,"round");
            var resolve = Cmd(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.Execute(0,resolve).Accepted, Is.True); Assert.That(game.Execute(0,resolve).Duplicate, Is.True);
            var view=game.View(null);
            Assert.That(view.Round, Is.EqualTo(2)); Assert.That(view.Turn, Is.EqualTo(1)); Assert.That(view.Phase, Is.EqualTo(Phase.Planning));
            Assert.That(view.Players.All(p => p.Level==1 && p.Gold==1 && p.HandCount==5 && p.DiscardColors.Count==0), Is.True);
            Assert.That(view.Events.Count(e => e.Kind=="CardRevealed"), Is.EqualTo(16));
            Assert.That(view.Players.All(p => !p.Plays.Any(play => play.Round == view.Round)), Is.True);
            Assert.That(view.Players.Sum(p => p.Plays.Count), Is.EqualTo(16));
            string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ResolveRoundEnd)).Code, Is.EqualTo("invalid_round_end"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Assert.That(LocalGameFactory.Restore(catalog,before).ExportSave(), Is.EqualTo(before));
        }
        [TestCase(1,2,0,1)]
        [TestCase(6,4,0,3)]
        [TestCase(28,8,0,6)]
        [TestCase(100,8,72,6)]
        public void AutomaticUpgradeSpendsCurrentLevelRepeatedlyAndPurpleIsNeverAHandCard(int gold, int level, int remainder, int choices)
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugSetGold,gold.ToString(),target:0); Apply(game,0,CommandKind.DebugAdvance,"round");
            Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.RoundEnd));
            Assert.That(game.View(null).Players[0].Level, Is.EqualTo(level)); Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(remainder));
            Assert.That(game.View(null).UpgradingSeats, Is.EqualTo(new[] {0}));
            Assert.That(game.View(null).UpgradeOptions, Is.Empty); Assert.That(game.View(1).UpgradeOptions, Is.Empty);
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); FinishUpgrades(game);
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.Round, Is.EqualTo(2)); Assert.That(state.Players[0].UpgradeHistory.Count, Is.EqualTo(choices));
            Assert.That(state.Players[0].PurpleCardId, level==8 ? Is.EqualTo("wasp-12-电闪雷鸣") : Is.Null);
            Assert.That(state.Players[0].Cards.Count, Is.EqualTo(5));
            Assert.That(state.Players[0].Cards.All(c => catalog.Card(c.CardId).Color!="purple" && c.Zone==CardZone.InHand), Is.True);
            Assert.That(state.Players.Skip(1).All(p => p.Gold==1 && p.Level==1), Is.True);
            Assert.That(state.Players[0].Gold, Is.EqualTo(remainder));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void CaptainChoosesOnlyTheLosingSideNonHeavyMinionsWithoutKillGold()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugRemoveMinion,game.View(null).Units.First(u => u.Team==Team.Red && u.Kind=="melee").Id);
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("round_minion_removal"));
            Assert.That(game.View(null).Pending!.ChooserSeat, Is.EqualTo(1)); Assert.That(game.View(null).RemainingMinionRemovals, Is.EqualTo(1));
            Assert.That(game.View(0).RoundMinionRemovals, Is.Empty);
            var options=game.View(1).RoundMinionRemovals; Assert.That(options.Count, Is.EqualTo(4));
            string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseRoundMinionRemoval,options[0])).Accepted, Is.False);
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.ChooseRoundMinionRemoval,BattlefieldTests.Heavy(game,Team.Red))).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            game=LocalGameFactory.Restore(catalog,before); Apply(game,1,CommandKind.ChooseRoundMinionRemoval,options[0]);
            Assert.That(game.View(null).Round, Is.EqualTo(2));
            Assert.That(game.View(null).Units.Count(u => u.Team==Team.Red && u.Kind!="hero"), Is.EqualTo(4));
            Assert.That(game.View(null).Players.All(p => p.Gold==1), Is.True);
            Assert.That(game.View(null).Events.Any(e => e.Kind=="GoldAwarded"), Is.False);
        }
        [Test]
        public void HeavyLossFinishesTheOldBattleBeforeSpawningThenResumesUpgrades()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            foreach(var minion in game.View(null).Units.Where(u => u.Team==Team.Red && u.Kind!="hero" && u.Kind!="heavy").ToList()) Apply(game,0,CommandKind.DebugRemoveMinion,minion.Id);
            Apply(game,0,CommandKind.DebugSetGold,"1",target:0); Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(2,-7));
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(null).RemainingMinionRemovals, Is.EqualTo(5));
            Apply(game,1,CommandKind.ChooseRoundMinionRemoval,BattlefieldTests.Heavy(game,Team.Red));
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("minion_spawn")); Assert.That(game.View(null).RemainingMinionRemovals, Is.EqualTo(0));
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,1,CommandKind.ChooseMinionSpawn,cell:new Hex(1,-7));
            Assert.That(game.View(null).Phase, Is.EqualTo(Phase.RoundEnd)); Assert.That(game.View(0).UpgradeOptions.Count, Is.EqualTo(6));
            Assert.That(game.View(null).Units.Count(u => u.Kind!="hero"), Is.EqualTo(11));
            FinishUpgrades(game);
            Assert.That(game.View(null).Round, Is.EqualTo(2)); Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(0));
            Assert.That(game.View(null).BlueMarks, Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void CompensationCanBuyAnUpgradeOnlyAtTheFollowingRoundEnd()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(null).UpgradingSeats, Is.EqualTo(new[] {0,1,2,3}));
            Assert.That(game.View(null).Players.All(p => p.Level==2 && p.Gold==0), Is.True);
            FinishUpgrades(game);
            Assert.That(game.View(null).Round, Is.EqualTo(3)); Assert.That(game.View(null).Players.All(p => p.Gold==0), Is.True);
        }
    }
}

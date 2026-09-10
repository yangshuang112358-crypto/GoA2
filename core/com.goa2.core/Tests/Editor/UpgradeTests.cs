using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class UpgradeTests
    {
        [Test]
        public void ThreeColorLadderAwardsEachRejectedIconWithItsSourceAndNeverTheChosenIcon()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugSetGold,"28",target:0); Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).UpgradeOptions.Count, Is.EqualTo(6));
            Apply(game,0,CommandKind.ChooseUpgrade,"wasp-03-电能波");
            Assert.That(game.View(0).UpgradeOptions.All(o => o.Color!="red" && o.CardLevel==2), Is.True);
            string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseUpgrade,"wasp-05-电能爆炸")).Code, Is.EqualTo("invalid_upgrade"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game,0,CommandKind.ChooseUpgrade,"wasp-08-偏转屏障"); Apply(game,0,CommandKind.ChooseUpgrade,"wasp-15-引力控制");
            Assert.That(game.View(0).UpgradeOptions.Count, Is.EqualTo(6)); Assert.That(game.View(0).UpgradeOptions.All(o => o.CardLevel==3), Is.True);
            foreach(string card in new[] {"wasp-05-电能爆炸","wasp-10-反射屏障","wasp-17-意念黑洞"})
            {
                game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,0,CommandKind.ChooseUpgrade,card);
            }
            var state=new JsonStateCodec().Read(game.ExportSave()); var player=state.Players[0];
            Assert.That(new[] {player.AttackBonus,player.DefenseBonus,player.MovementBonus,player.InitiativeBonus,player.RangeBonus,player.RangedBonus}, Is.EqualTo(new[] {1,1,1,1,1,1}));
            Assert.That(player.UpgradeHistory.Select(h => h.HeroLevel), Is.EqualTo(new[] {2,3,4,5,6,7}));
            Assert.That(player.UpgradeHistory.Select(h => h.RejectedCardId), Is.EqualTo(new[] {"wasp-02-回旋镖","wasp-09-意念操控","wasp-14-动力助推","wasp-04-雷霆回旋镖","wasp-11-心灵控制","wasp-16-动能震爆"}));
            Assert.That(player.UpgradeHistory.All(h => h.Round==1 && h.Amount==1), Is.True);
            Assert.That(game.View(null).Events.Any(e => e.Kind=="CardUpgraded" || e.Kind=="UpgradeBonusGranted"), Is.False);
            Assert.That(game.View(0).Events.Count(e => e.Kind=="UpgradeBonusGranted"), Is.EqualTo(6));
            Assert.That(game.View(null).Events.Count(e => e.Kind=="PurpleCardGranted"), Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void PlayersChooseIndependentlyAndWrongSeatOrDuplicateNeverSpendsTwice()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugSetGold,"3",target:0); Apply(game,0,CommandKind.DebugSetGold,"1",target:1);
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(null).UpgradingSeats, Is.EqualTo(new[] {0,1})); Assert.That(game.View(2).UpgradeOptions, Is.Empty);
            string before=game.ExportSave();
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.ChooseUpgrade,"wasp-02-回旋镖")).Code, Is.EqualTo("invalid_upgrade"));
            Assert.That(game.Execute(2,Cmd(game,2,CommandKind.ChooseUpgrade,"brogan-02-投掷飞斧")).Code, Is.EqualTo("invalid_upgrade"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            var choose=Cmd(game,1,CommandKind.ChooseUpgrade,"shargatha-02-快速突刺");
            Assert.That(game.Execute(1,choose).Accepted, Is.True); Assert.That(game.Execute(1,choose).Duplicate, Is.True);
            Assert.That(game.View(null).UpgradingSeats, Is.EqualTo(new[] {0})); Assert.That(game.View(null).Round, Is.EqualTo(1));
            Apply(game,0,CommandKind.DebugGold,"100",target:0);
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); RoundEndTests.FinishUpgrades(game);
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.Round, Is.EqualTo(2)); Assert.That(state.Players[0].Level, Is.EqualTo(3)); Assert.That(state.Players[0].Gold, Is.EqualTo(100));
            Assert.That(state.Players[1].UpgradeHistory.Count, Is.EqualTo(1)); Assert.That(state.Players[1].DefenseBonus, Is.EqualTo(1));
            Assert.That(state.Players[1].InitiativeBonus, Is.EqualTo(0));
            Assert.That(game.View(0).OwnUpgradeHistory.Count, Is.EqualTo(2)); Assert.That(game.View(1).OwnUpgradeHistory.Count, Is.EqualTo(1));
            Assert.That(game.View(null).OwnUpgradeHistory, Is.Empty);
        }
        [TestCase("shargatha-02-快速突刺",0,1)]
        [TestCase("shargatha-03-致命横扫",1,0)]
        public void QuickThrustPassiveOnlyAppliesWhenTheCardIsRejected(string selected, int initiative, int defense)
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugSetGold,"1",target:1); Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Apply(game,1,CommandKind.ChooseUpgrade,selected);
            var player=new JsonStateCodec().Read(game.ExportSave()).Players[1];
            Assert.That(player.InitiativeBonus, Is.EqualTo(initiative)); Assert.That(player.DefenseBonus, Is.EqualTo(defense));
            Assert.That(player.Cards.Single(c => catalog.Card(c.CardId).Color=="red").CardId, Is.EqualTo(selected));
        }
        [Test]
        public void MaximumLevelCompensatesWithoutASecondPurpleOrExtraColorChoice()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugSetGold,"100",target:0); Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            RoundEndTests.FinishUpgrades(game);
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).UpgradeOptions, Is.Empty); Assert.That(game.View(null).UpgradingSeats, Is.EqualTo(new[] {1,2,3}));
            RoundEndTests.FinishUpgrades(game);
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.Players[0].Gold, Is.EqualTo(73)); Assert.That(state.Players[0].UpgradeHistory.Count, Is.EqualTo(6));
            Assert.That(state.Events.Count(e => e.Kind=="PurpleCardGranted" && e.Seat==0), Is.EqualTo(1));
            Assert.That(state.Round, Is.EqualTo(3));
        }
        [Test]
        public void SandboxEquipmentDoesNotConsumeTheFormalUpgradeLadder()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugEquipCard,"wasp-04-雷霆回旋镖",target:0); Apply(game,0,CommandKind.DebugSetGold,"28",target:0);
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            var option=game.View(0).UpgradeOptions.Single(o => o.CardId=="wasp-02-回旋镖");
            Assert.That(option.PreviousCardId, Is.EqualTo("wasp-04-雷霆回旋镖")); Assert.That(option.CardLevel, Is.EqualTo(2));
            Apply(game,0,CommandKind.ChooseUpgrade,option.CardId); RoundEndTests.FinishUpgrades(game);
            Assert.That(game.View(0).OwnUpgradeHistory.Count, Is.EqualTo(6)); Assert.That(game.View(null).Round, Is.EqualTo(2));
        }
        [Test]
        public void PendingRoundSettlementRejectsStructuralDebugMutationButKeepsResourcesAvailable()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugSetGold,"1",target:0); Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).DebugTeleports.Values.All(c => c.Count==0), Is.True);
            string before=game.ExportSave();
            var minion=game.View(null).Units.First(u => u.Kind=="melee");
            foreach (var command in new[] {
                Cmd(game,0,CommandKind.DebugRemoveMinion,minion.Id),
                Cmd(game,0,CommandKind.DebugDefeatHero,"hero:1",target:0),
                Cmd(game,0,CommandKind.DebugDiscard,"wasp-00-闪耀之刃",target:0) })
                Assert.That(game.Execute(0,command).Code, Is.EqualTo("pending_round_end"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game,0,CommandKind.DebugGold,"2",target:0);
            Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(2));
        }
    }
}

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
    public sealed class OneManArmyTests
    {
        internal const string Card="brogan-12-一人成军";
        internal static GameSession Ready(ContentCatalog catalog,bool owned=true,int engine=GameState.CurrentEngineVersion,int broganSeat=0,int removed=4)
        {
            var game=LocalGameFactory.Create(catalog,"one-man-army",new[]{"A","B","C","D"},42,true,engine);
            var heroes=new[]{"brogan","wasp","arien","sabina"};heroes[0]=heroes[broganSeat];heroes[broganSeat]="brogan";
            Apply(game,0,CommandKind.DebugPrepare,string.Join(",",heroes));
            if(owned){Apply(game,0,CommandKind.DebugSetGold,"28",target:broganSeat);Apply(game,0,CommandKind.DebugAdvance,"round");Apply(game,0,CommandKind.ResolveRoundEnd);
                for(int i=0;i<7;i++)Apply(game,broganSeat,CommandKind.ChooseUpgrade,game.View(broganSeat).UpgradeOptions.First().CardId);}
            foreach(var minion in game.View(0).Units.Where(u=>u.Team==game.View(0).Players[broganSeat].Team && u.Kind=="melee").Take(removed).ToList())
                Apply(game,0,CommandKind.DebugRemoveMinion,minion.Id);
            var free=catalog.Cells.First(c=>!c.Obstacle && c.Region!=game.View(0).CombatRegion && !game.View(0).Units.Any(u=>u.Position==c.Position));
            Apply(game,0,CommandKind.DebugTeleport,"hero:"+broganSeat,cell:free.Position);
            for(int i=0;i<4;i++)Apply(game,0,CommandKind.DebugSetGold,"0",target:i);
            Apply(game,0,CommandKind.DebugAdvance,"round");return game;
        }

        [Test]
        public void CountsTwoFromAnyRegionAndSelectingHeroImmediatelyPreservesAllRemainingMinions()
        {
            var catalog=BattlefieldTests.Catalog();var game=Ready(catalog);var before=game.View(0);int round=before.Round;
            Assert.That(catalog.Cell(before.Units.Single(u=>u.Seat==0).Position).Region,Is.Not.EqualTo(before.CombatRegion));
            Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).Events.Last(e=>e.Kind=="MinionBattleCounted").Detail,Is.EqualTo("4:6:2"));
            Assert.That(game.View(0).RoundMinionRemovals,Does.Contain("hero:0"));
            game=ChargeTests.Restore(catalog,game);Apply(game,0,CommandKind.ChooseRoundMinionRemoval,"hero:0");
            Assert.That(game.View(0).Units.Select(u=>u.Id),Is.EquivalentTo(before.Units.Select(u=>u.Id)));
            Assert.That(game.View(0).Round,Is.EqualTo(round+1));Assert.That(game.View(0).CombatRegion,Is.EqualTo(before.CombatRegion));
            Assert.That(game.View(0).BlueCrystal,Is.EqualTo(before.BlueCrystal));Assert.That(game.View(0).BlueMarks,Is.EqualTo(before.BlueMarks));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="GoldAwarded" || e.Kind=="HeroDefeated"),Is.False);ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void RangedFirstThenHeroDoesNotUndoRemovalAndHeavyRemainsProtected()
        {
            var catalog=BattlefieldTests.Catalog();var game=Ready(catalog);
            string ranged=game.View(0).Units.Single(u=>u.Team==Team.Blue && u.Kind=="ranged").Id;
            string heavy=BattlefieldTests.Heavy(game,Team.Blue);Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).RoundMinionRemovals,Does.Not.Contain(heavy));Apply(game,0,CommandKind.ChooseRoundMinionRemoval,ranged);
            Assert.That(game.View(0).RemainingMinionRemovals,Is.EqualTo(1));Assert.That(game.View(0).RoundMinionRemovals,Is.EqualTo(new[]{"hero:0"}));
            game=ChargeTests.Restore(catalog,game);Apply(game,0,CommandKind.ChooseRoundMinionRemoval,"hero:0");
            Assert.That(game.View(0).Units.Any(u=>u.Id==ranged),Is.False);Assert.That(game.View(0).Units.Any(u=>u.Id==heavy),Is.True);
            ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void CaptainChoosesBroganEvenWhenBroganIsTheOtherTeammate()
        {
            var game=Ready(BattlefieldTests.Catalog(),broganSeat:2);Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).Pending?.ChooserSeat,Is.EqualTo(0));Assert.That(game.View(2).RoundMinionRemovals,Is.Empty);
            string before=game.ExportSave();Assert.That(game.Execute(2,Cmd(game,2,CommandKind.ChooseRoundMinionRemoval,"hero:2")).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));Apply(game,0,CommandKind.ChooseRoundMinionRemoval,"hero:2");
            Assert.That(game.View(0).Units.Single(u=>u.Seat==2).Kind,Is.EqualTo("hero"));
        }

        [TestCase(false,GameState.CurrentEngineVersion)] [TestCase(true,58)]
        public void UnownedOrOlderEngineRetainsOrdinaryCounts(bool owned,int engine)
        {
            var game=Ready(BattlefieldTests.Catalog(),owned,engine);Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).RemainingMinionRemovals,Is.EqualTo(4));Assert.That(game.View(0).RoundMinionRemovals,Does.Not.Contain("hero:0"));
        }

        [TestCase(2,"6:6:0")] [TestCase(0,"8:6:2")]
        public void TieOrWinningSideUsesContributionWithoutMakingHeroAnEnemyRemovalOption(int removed,string counts)
        {
            var game=Ready(BattlefieldTests.Catalog(),removed:removed);Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).Events.Last(e=>e.Kind=="MinionBattleCounted").Detail,Is.EqualTo(counts));
            if(removed==2)Assert.That(game.View(0).Pending,Is.Null);
            else{Assert.That(game.View(1).Pending?.ChooserSeat,Is.EqualTo(1));Assert.That(game.View(1).RoundMinionRemovals,Does.Not.Contain("hero:0"));}
        }

        [Test]
        public void IllegalChoiceAndDuplicateReplacementHaveNoAdditionalEffects()
        {
            var game=Ready(BattlefieldTests.Catalog());Apply(game,0,CommandKind.ResolveRoundEnd);string before=game.ExportSave();
            foreach(string id in new[]{"hero:1","hero:2",BattlefieldTests.Heavy(game,Team.Blue)})
            {Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseRoundMinionRemoval,id)).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var command=Cmd(game,0,CommandKind.ChooseRoundMinionRemoval,"hero:0");Assert.That(game.Execute(0,command).Accepted,Is.True);
            string after=game.ExportSave();Assert.That(game.Execute(0,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
        }

        [Test]
        public void DefeatedBroganDoesNotContributeAndDoesNotBecomeARemovalChoice()
        {
            var game=Ready(BattlefieldTests.Catalog());Apply(game,0,CommandKind.DebugSetCrystal,"30",target:0);
            Apply(game,0,CommandKind.DebugDefeatHero,"hero:0",target:1);Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).RemainingMinionRemovals,Is.EqualTo(4));Assert.That(game.View(0).RoundMinionRemovals,Does.Not.Contain("hero:0"));
        }

        [Test]
        public void VirtualContributionDoesNotProtectHeavyFromOrdinaryAttacks()
        {
            var game=Ready(BattlefieldTests.Catalog());
            var ranged=game.View(0).Units.Single(u=>u.Team==Team.Blue && u.Kind=="ranged");Apply(game,0,CommandKind.DebugRemoveMinion,ranged.Id);
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(GameRules.LegalMinionRemovals(state),Does.Contain(BattlefieldTests.Heavy(game,Team.Blue)));
            Assert.That(GameRules.LegalMinionRemovals(state),Does.Not.Contain("hero:0"));
        }

        [TestCase(1)] [TestCase(3)]
        public void RedTeamUsesItsOwnCaptainAndContribution(int broganSeat)
        {
            var catalog=BattlefieldTests.Catalog();var game=Ready(catalog,broganSeat:broganSeat);Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(1).Events.Last(e=>e.Kind=="MinionBattleCounted").Detail,Is.EqualTo("6:4:2"));
            Assert.That(game.View(1).Pending?.ChooserSeat,Is.EqualTo(1));Assert.That(game.View(0).RoundMinionRemovals,Is.Empty);
            Apply(game,1,CommandKind.ChooseRoundMinionRemoval,"hero:"+broganSeat);ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void DifferenceOfOneCanBePaidByAMinionWithoutForcingTheHeroReplacement()
        {
            var catalog=BattlefieldTests.Catalog();var game=Ready(catalog,removed:3);Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).RemainingMinionRemovals,Is.EqualTo(1));
            string melee=game.View(0).Units.First(u=>u.Team==Team.Blue && u.Kind=="melee").Id;
            Apply(game,0,CommandKind.ChooseRoundMinionRemoval,melee);
            Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Planning));Assert.That(game.View(0).Events.Any(e=>e.Kind=="MinionBattleStoppedByHero"),Is.False);
            ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void ChoosingHeroContinuesUpgradesAndGrantsCompensationExactlyOnce()
        {
            var catalog=BattlefieldTests.Catalog();var game=Ready(catalog);Apply(game,0,CommandKind.DebugSetGold,"1",target:2);
            int recalls=game.View(0).Events.Count(e=>e.Kind=="CardsRecalled"),compensation=game.View(0).Events.Count(e=>e.Kind=="RoundCompensationGranted");
            Apply(game,0,CommandKind.ResolveRoundEnd);Apply(game,0,CommandKind.ChooseRoundMinionRemoval,"hero:0");
            Assert.That(game.View(2).RoundEndStage,Is.EqualTo("upgrades"));Assert.That(game.View(2).UpgradeOptions,Is.Not.Empty);
            game=ChargeTests.Restore(catalog,game);RoundEndTests.FinishUpgrades(game);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardsRecalled")-recalls,Is.EqualTo(4));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="RoundCompensationGranted")-compensation,Is.EqualTo(3));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="MinionBattleStoppedByHero"),Is.EqualTo(1));ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void HeroAloneRemainsAValidBattleParticipantInADomainFixture()
        {
            var catalog=BattlefieldTests.Catalog();var codec=new JsonStateCodec();var state=codec.Read(Ready(catalog).ExportSave());
            // Domain fixture isolates the empty-minion boundary; not presented as a replayable match.
            state.Units.RemoveAll(u=>u.Team==Team.Blue && u.Kind!="hero");
            var game=new GameSession(catalog,codec,state);Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(0).RemainingMinionRemovals,Is.EqualTo(4));Assert.That(game.View(0).RoundMinionRemovals,Is.EqualTo(new[]{"hero:0"}));
            Apply(game,0,CommandKind.ChooseRoundMinionRemoval,"hero:0");Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Planning));
        }

        [Test]
        public void CapturedRoleTracksSourceAndOwnershipWithoutLeakingChoiceOrChangingHeroIdentity()
        {
            var catalog=BattlefieldTests.Catalog();var game=Ready(catalog);Apply(game,0,CommandKind.ResolveRoundEnd);
            var state=new JsonStateCodec().Read(game.ExportSave());var role=state.RoundEnd.HeroContributions.Single();
            Assert.That(role.SourceCardId,Is.EqualTo(Card));Assert.That(role.Count,Is.EqualTo(2));Assert.That(role.ControllerSeat,Is.EqualTo(0));
            Assert.That(role.ProgramVersion,Is.EqualTo(1));Assert.That(role.ProgramId,Is.EqualTo("two_minions_stop_battle_on_removal"));
            Assert.That(state.Units.Single(u=>u.Id==role.UnitId).Kind,Is.EqualTo("hero"));
            // Board-unit candidates are public; only the captain receives actionable removals.
            Assert.That(game.View(1).RoundMinionRemovals,Is.Empty);Assert.That(game.View(null).RoundMinionRemovals,Is.Empty);
            string before=game.ExportSave();Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DebugTeleport,"hero:0",destination:new Hex(0,-1))).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void ExactBindingAndHistoricalBattleSaveBytesArePreserved()
        {
            var catalog=BattlefieldTests.Catalog();var card=catalog.Card(Card);
            Assert.That(UltimateRules.HasProgram(card),Is.True);Assert.That(UltimateRules.HasProgram(card,58),Is.False);
            card.Text+="修订";Assert.That(UltimateRules.HasProgram(card),Is.False);
            catalog=BattlefieldTests.Catalog();var old=Ready(catalog,engine:58);Apply(old,0,CommandKind.ResolveRoundEnd);string bytes=old.ExportSave();
            Assert.That(bytes,Does.Not.Contain("HeroContributions"));Assert.That(ChargeTests.Restore(catalog,old).ExportSave(),Is.EqualTo(bytes));
        }
    }
}

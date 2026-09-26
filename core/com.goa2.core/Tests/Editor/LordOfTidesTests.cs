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
    public sealed class LordOfTidesTests
    {
        internal const string Card="arien-12-潮汐之主", Basic="arien-06-打断施法";
        internal static GameSession Ready(ContentCatalog catalog,bool owned=true,bool brogan=false,int engine=GameState.CurrentEngineVersion,
            bool outside=false,int removed=0,Team losing=Team.Blue,bool blockedSpawn=false,string played=Basic)
        {
            var game=LocalGameFactory.Create(catalog,"lord-of-tides",new[]{"A","B","C","D"},42,true,engine);
            Apply(game,0,CommandKind.DebugPrepare,"arien,wasp,brogan,sabina");
            if(owned || brogan)
            {
                if(owned)Apply(game,0,CommandKind.DebugSetGold,"28",target:0);
                if(brogan)Apply(game,0,CommandKind.DebugSetGold,"28",target:2);
                Apply(game,0,CommandKind.DebugAdvance,"round");Apply(game,0,CommandKind.ResolveRoundEnd);RoundEndTests.FinishUpgrades(game);
            }
            foreach(var u in game.View(0).Units.Where(u=>u.Team==losing && u.Kind!="hero" && u.Kind!="heavy").OrderBy(u=>u.Kind=="ranged"?1:0).Take(removed).ToList())Apply(game,0,CommandKind.DebugRemoveMinion,u.Id);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:outside?new Hex(5,-8):new Hex(-2,0));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-8));
            var obstacleSpawn=blockedSpawn?catalog.Cells.First(c=>c.Region=="blueNear" && c.Spawn.Contains("Spawn") && !c.Spawn.Contains("Hero")):null;
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:obstacleSpawn?.Position ?? new Hex(6,-5));
            if(!game.View(0).OwnCards.Any(c=>c.CardId==played))Apply(game,0,CommandKind.DebugEquipCard,played,target:0);
            var cards=new[]{played,"wasp-00-闪耀之刃","brogan-00-猛攻","sabina-00-近身射击"};
            for(int i=0;i<4;i++)Apply(game,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(game,0);return game;
        }
        static void Offer(GameSession g){Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).Pending?.ResumeAt,Is.EqualTo("action_minion_battle_offer"));}

        [TestCase("skip")] [TestCase("battle")]
        public void OptionalTieBattleKeepsTheSkillAndDoesNotRunRoundEnd(string option)
        {
            var catalog=BattlefieldTests.Catalog();var g=Ready(catalog);var before=g.View(0);
            int resolved=before.Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Basic),recalls=before.Events.Count(e=>e.Kind=="CardsRecalled");
            Offer(g);Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Basic),Is.True);
            Assert.That(g.View(0).PrimaryOptions,Is.EquivalentTo(new[]{"battle","skip"}));Assert.That(g.View(1).PrimaryOptions,Is.Empty);
            g=ChargeTests.Restore(catalog,g);Apply(g,0,CommandKind.ChoosePrimaryOption,option);
            Assert.That(g.View(0).Round,Is.EqualTo(before.Round));Assert.That(g.View(0).Turn,Is.EqualTo(before.Turn));
            Assert.That(g.View(0).Players.Select(p=>p.Gold),Is.EqualTo(before.Players.Select(p=>p.Gold)));
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardsRecalled"),Is.EqualTo(recalls));
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Basic),Is.EqualTo(resolved+1));
            Assert.That(new JsonStateCodec().Read(g.ExportSave()).RoundEnd,Is.Null);ChargeTests.Restore(catalog,g);
        }

        [TestCase(false,59,false)] [TestCase(true,59,false)] [TestCase(true,GameState.CurrentEngineVersion,true)] [TestCase(false,GameState.CurrentEngineVersion,false)]
        public void UnownedOldEngineAndOutsideCurrentBattleRegionDoNotOffer(bool owned,int engine,bool outside)
        {
            var g=Ready(BattlefieldTests.Catalog(),owned:owned,engine:engine,outside:outside);Apply(g,0,CommandKind.BeginPrimary);
            Assert.That(g.View(0).Pending?.Source,Is.Not.EqualTo(Card));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UltimateTriggered" && e.CardId==Card),Is.False);
        }

        [TestCase(Team.Blue,0)] [TestCase(Team.Red,1)]
        public void LosingTeamCaptainRemovesOneMinionThenTheSameActionResumes(Team losing,int captain)
        {
            var catalog=BattlefieldTests.Catalog();var g=Ready(catalog,removed:1,losing:losing);Offer(g);Apply(g,0,CommandKind.ChoosePrimaryOption,"battle");
            Assert.That(g.View(captain).Pending?.Kind,Is.EqualTo("action_minion_removal"));Assert.That(g.View(captain).Pending.ChooserSeat,Is.EqualTo(captain));
            Assert.That(g.View(captain).RemainingMinionRemovals,Is.EqualTo(1));g=ChargeTests.Restore(catalog,g);
            string target=g.View(captain).RoundMinionRemovals.First();var before=g.View(0);Apply(g,captain,CommandKind.ChooseRoundMinionRemoval,target);
            Assert.That(g.View(0).Units.Any(u=>u.Id==target),Is.False);Assert.That(g.View(0).Round,Is.EqualTo(before.Round));
            Assert.That(g.View(0).Events.Any(e=>e.Kind=="GoldAwarded"),Is.False);ChargeTests.Restore(catalog,g);
        }

        [TestCase(false)] [TestCase(true)]
        public void OneManArmyOutsideRegionStopsRemainingRemovalsAndResumesArien(bool rangedFirst)
        {
            var catalog=BattlefieldTests.Catalog();var g=Ready(catalog,brogan:true,removed:4);Offer(g);Apply(g,0,CommandKind.ChoosePrimaryOption,"battle");
            Assert.That(g.View(0).RemainingMinionRemovals,Is.EqualTo(2));Assert.That(g.View(0).RoundMinionRemovals,Does.Contain("hero:2"));
            string ranged=g.View(0).Units.Single(u=>u.Team==Team.Blue && u.Kind=="ranged").Id;
            if(rangedFirst){Apply(g,0,CommandKind.ChooseRoundMinionRemoval,ranged);Assert.That(g.View(0).RoundMinionRemovals,Is.EqualTo(new[]{"hero:2"}));}
            g=ChargeTests.Restore(catalog,g);Apply(g,0,CommandKind.ChooseRoundMinionRemoval,"hero:2");
            Assert.That(g.View(0).CombatRegion,Is.EqualTo("mid"));Assert.That(g.View(0).Units.Any(u=>u.Id==ranged),Is.EqualTo(!rangedFirst));
            Assert.That(g.View(0).Units.Single(u=>u.Id=="hero:2").Kind,Is.EqualTo("hero"));Assert.That(new JsonStateCodec().Read(g.ExportSave()).Execution,Is.Null);
            ChargeTests.Restore(catalog,g);
        }

        [Test]
        public void HeavyRemovalWaitsForSpawnThenResumesWithoutRecallUpgradeOrReward()
        {
            var catalog=BattlefieldTests.Catalog();var g=Ready(catalog,removed:5,blockedSpawn:true);var before=g.View(0);Offer(g);Apply(g,0,CommandKind.ChoosePrimaryOption,"battle");
            Apply(g,0,CommandKind.ChooseRoundMinionRemoval,BattlefieldTests.Heavy(g,Team.Blue));Assert.That(g.View(0).Pending?.Kind,Is.EqualTo("minion_spawn"));
            Assert.That(new JsonStateCodec().Read(g.ExportSave()).Execution,Is.Not.Null);g=ChargeTests.Restore(catalog,g);
            for(int i=0;i<12 && g.View(0).Pending?.Kind=="minion_spawn";i++)
            {int seat=g.View(0).Pending.ChooserSeat;var state=new JsonStateCodec().Read(g.ExportSave());Apply(g,seat,CommandKind.ChooseMinionSpawn,cell:GameRules.LegalMinionSpawns(catalog,state,seat).First());}
            Assert.That(new JsonStateCodec().Read(g.ExportSave()).Execution,Is.Null);Assert.That(g.View(0).CombatRegion,Is.EqualTo("blueNear"));
            Assert.That(g.View(0).Round,Is.EqualTo(before.Round));Assert.That(g.View(0).Players.Select(p=>p.Gold),Is.EqualTo(before.Players.Select(p=>p.Gold)));
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardsRecalled"),Is.EqualTo(before.Events.Count(e=>e.Kind=="CardsRecalled")));ChargeTests.Restore(catalog,g);
        }

        [Test]
        public void InvalidChoicesAndDuplicateBattleOfferAreAtomic()
        {
            var g=Ready(BattlefieldTests.Catalog(),removed:1,losing:Team.Red);Offer(g);string before=g.ExportSave();
            foreach(var command in new[]{Cmd(g,1,CommandKind.ChoosePrimaryOption,"battle"),Cmd(g,0,CommandKind.ChoosePrimaryOption,"invalid"),Cmd(g,0,CommandKind.DebugSetGold,"100",target:0)})
            {Assert.That(g.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            var battle=Cmd(g,0,CommandKind.ChoosePrimaryOption,"battle");Assert.That(g.Execute(0,battle).Accepted,Is.True);before=g.ExportSave();
            Assert.That(g.Execute(0,battle).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));
            var illegal=Cmd(g,0,CommandKind.ChooseRoundMinionRemoval,g.View(1).RoundMinionRemovals.First());Assert.That(g.Execute(0,illegal).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
        }

        [Test]
        public void NonBasicSkillDoesNotOfferTheUltimate()
        {
            var catalog=BattlefieldTests.Catalog();var g=Ready(catalog,played:"arien-11-潮汐之力");Apply(g,0,CommandKind.BeginPrimary);
            Apply(g,0,CommandKind.ChoosePlacement,cell:g.View(0).Placements.First());
            Assert.That(g.View(0).Pending?.Source,Is.Not.EqualTo(Card));Assert.That(g.View(0).Events.Any(e=>e.Kind=="UltimateTriggered" && e.CardId==Card),Is.False);ChargeTests.Restore(catalog,g);
        }

        [Test]
        public void PushVictoryClearsTheSavedParentWithoutUpgradingInADomainFixture()
        {
            var catalog=BattlefieldTests.Catalog();var codec=new JsonStateCodec();var s=codec.Read(Ready(catalog,removed:5).ExportSave());
            s.RedMarks=s.VictoryMarksRequired-1;var g=new GameSession(catalog,codec,s);int upgrades=g.View(0).Events.Count(e=>e.Kind=="UpgradesStarted");
            Offer(g);Apply(g,0,CommandKind.ChoosePrimaryOption,"battle");Apply(g,0,CommandKind.ChooseRoundMinionRemoval,BattlefieldTests.Heavy(g,Team.Blue));
            Assert.That(g.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(g.View(0).Winner,Is.EqualTo(Team.Red));
            s=codec.Read(g.ExportSave());Assert.That(s.Execution,Is.Null);Assert.That(s.Pending,Is.Null);Assert.That(s.Frontline,Is.Null);
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="UpgradesStarted"),Is.EqualTo(upgrades));
        }

        [Test]
        public void BindingRequiresExactTextAndOlderSaveBytesRemainStable()
        {
            var catalog=BattlefieldTests.Catalog();var card=catalog.Card(Card);Assert.That(UltimateRules.HasProgram(card),Is.True);
            Assert.That(UltimateRules.HasProgram(card,59),Is.False);card.Text+="修改";Assert.That(UltimateRules.HasProgram(card),Is.False);
            catalog=BattlefieldTests.Catalog();var g=Ready(catalog,engine:59);Apply(g,0,CommandKind.BeginPrimary);string bytes=g.ExportSave();
            Assert.That(bytes,Does.Not.Contain("ActionBattle"));Assert.That(ChargeTests.Restore(catalog,g).ExportSave(),Is.EqualTo(bytes));
        }

        [Test]
        public void IllegalBattleUnitsAndDuplicateRemovalDoNotConsumeExtraMinions()
        {
            var catalog=BattlefieldTests.Catalog();var g=Ready(catalog,removed:1);Offer(g);Apply(g,0,CommandKind.ChoosePrimaryOption,"battle");
            string before=g.ExportSave();var enemy=g.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee");
            foreach(string id in new[]{"hero:0","hero:2",enemy.Id,BattlefieldTests.Heavy(g,Team.Blue)})
            {Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseRoundMinionRemoval,id)).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            var remove=Cmd(g,0,CommandKind.ChooseRoundMinionRemoval,g.View(0).RoundMinionRemovals.First());Assert.That(g.Execute(0,remove).Accepted,Is.True);before=g.ExportSave();
            Assert.That(g.Execute(0,remove).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));
            Assert.That(g.View(0).Effects.Any(e=>e.SourceCardId==Basic),Is.True);ChargeTests.Restore(catalog,g);
        }

        [Test]
        public void SilverUsedForDefenseDoesNotOfferABattle()
        {
            var catalog=BattlefieldTests.Catalog();var g=Ready(catalog,played:"arien-11-潮汐之力");
            Apply(g,0,CommandKind.DebugAdvance,"round");Apply(g,0,CommandKind.ResolveRoundEnd);RoundEndTests.FinishUpgrades(g);
            Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(-1,0));
            var cards=new[]{"arien-00-华丽刀锋","wasp-00-闪耀之刃","brogan-00-猛攻","sabina-00-近身射击"};
            for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,3);
            Apply(g,3,CommandKind.BeginPrimary);Apply(g,3,CommandKind.ChooseAttackTarget,"hero:0");Apply(g,0,CommandKind.Defend,Basic);
            Assert.That(g.View(0).Events.Any(e=>e.Kind=="UltimateTriggered" && e.CardId==Card),Is.False);ChargeTests.Restore(catalog,g);
        }

        [Test]
        public void OrdinaryHeavyPushResumesImmediatelyWhenThereAreNoOccupiedSpawnPoints()
        {
            var catalog=BattlefieldTests.Catalog();var g=Ready(catalog,removed:5);int round=g.View(0).Round;
            int resolved=g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Basic);Offer(g);Apply(g,0,CommandKind.ChoosePrimaryOption,"battle");
            Apply(g,0,CommandKind.ChooseRoundMinionRemoval,BattlefieldTests.Heavy(g,Team.Blue));
            Assert.That(g.View(0).CombatRegion,Is.EqualTo("blueNear"));Assert.That(g.View(0).Round,Is.EqualTo(round));
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Basic),Is.EqualTo(resolved+1));
            Assert.That(new JsonStateCodec().Read(g.ExportSave()).Execution,Is.Null);ChargeTests.Restore(catalog,g);
        }
    }
}

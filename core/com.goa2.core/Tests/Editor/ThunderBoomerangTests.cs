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
    public sealed class ThunderBoomerangTests
    {
        internal const string Thunder="wasp-04-雷霆回旋镖",Dodge="tigerclaw-18-躲闪";
        internal static GameSession Setup(ContentCatalog catalog)
        {
            var game=LocalGameFactory.Create(catalog,"thunder",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"wasp,tigerclaw,brogan,arien");Apply(game,0,CommandKind.DebugEquipCard,Thunder,target:0);Apply(game,0,CommandKind.DebugEquipCard,Dodge,target:1);Apply(game,0,CommandKind.DebugEquipCard,"arien-13-挑战者",target:3);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(5,-8));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-9));Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-6));
            Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(6,-7));
            string[] cards={Thunder,"tigerclaw-07-伺机待发","brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(0));return game;
        }
        private static GameSession Restore(ContentCatalog catalog,GameSession game)
        {string save=game.ExportSave();var result=LocalGameFactory.Restore(catalog,save);Assert.That(result.ExportSave(),Is.EqualTo(save));return result;}
        private static void FirstAttack(GameSession game) {Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");}
        private static void DefeatFirst(GameSession game) {FirstAttack(game);Apply(game,1,CommandKind.DeclineDefense);}
        [Test]
        public void ExactContractAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Thunder);Assert.That(card.PrimaryValue,Is.EqualTo(4));Assert.That(card.Initiative,Is.EqualTo(9));
            Assert.That(card.SubtypeValue,Is.EqualTo(3));Assert.That(card.SecondaryMovement,Is.EqualTo(4));Assert.That(card.SecondaryDefense,Is.EqualTo(4));
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,18),Is.False);
        }
        [Test]
        public void EachHeroDefeatAllowsAnotherOptionalAttackWithoutAnotherPrimaryAction()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);FirstAttack(game);game=Restore(catalog,game);
            var decline=Cmd(game,1,CommandKind.DeclineDefense);Assert.That(game.Execute(1,decline).Accepted,Is.True);game=Restore(catalog,game);
            var view=game.View(0);Assert.That(view.Pending!.Kind,Is.EqualTo("attack_target"));Assert.That(view.Pending.Optional,Is.True);Assert.That(view.Pending.ResumeAt,Is.EqualTo("repeat_attack"));Assert.That(view.ActiveSeat,Is.EqualTo(0));
            Assert.That(view.AttackTargets,Does.Not.Contain("hero:1"));Assert.That(view.AttackTargets,Does.Contain("hero:3"));
            string once=game.ExportSave();Assert.That(game.Execute(1,decline).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(once));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:3");game=Restore(catalog,game);
            Assert.That(game.View(3).Attack!.TargetUnitId,Is.EqualTo("hero:3"));Assert.That(game.View(3).Attack!.FinalAttack,Is.EqualTo(3));Assert.That(game.View(3).Attack!.FriendlyGuard,Is.EqualTo(1));
            Apply(game,3,CommandKind.DeclineDefense);game=Restore(catalog,game);Assert.That(game.View(0).Pending!.Optional,Is.True);
            Assert.That(game.View(0).AttackTargets,Is.EqualTo(new[]{"minion:-1,-3"}));
            var minion=Cmd(game,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");Assert.That(game.Execute(0,minion).Accepted,Is.True);game=Restore(catalog,game);
            string after=game.ExportSave();Assert.That(game.Execute(0,minion).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            view=game.View(0);Assert.That(view.ActiveSeat,Is.EqualTo(2));Assert.That(view.RedCrystal,Is.EqualTo(5));Assert.That(view.Players[0].Gold,Is.EqualTo(4));Assert.That(view.Players[2].Gold,Is.EqualTo(2));
            Assert.That(view.Attack,Is.Null);Assert.That(view.Events.Count(e=>e.Kind=="PrimaryActionStarted"),Is.EqualTo(1));Assert.That(view.Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(3));
            Assert.That(view.Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(3));Assert.That(view.Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Thunder),Is.EqualTo(1));
        }
        [Test]
        public void OptionalStopIsOwnedByAttackerAndInitialAttackCannotBeSkipped()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);
            string initial=game.ExportSave();Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseAttackTarget,"skip")).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(initial));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);game=Restore(catalog,game);
            string repeat=game.ExportSave();
            foreach(var command in new[]{Cmd(game,1,CommandKind.ChooseAttackTarget,"skip"),Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:1"),Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:2"),Cmd(game,1,CommandKind.ChooseAttackTarget,"hero:3")})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(repeat));}
            Apply(game,0,CommandKind.ChooseAttackTarget,"skip");game=Restore(catalog,game);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(1));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(1));
        }
        [TestCase(true)] [TestCase(false)]
        public void BlockStopsButInsufficientNumericDefenseAllowsRepeat(bool block)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);FirstAttack(game);
            string response=block?Dodge:"tigerclaw-02-偷袭";Assert.That(game.View(1).DefenseOptions.Single(d=>d.CardId==response).Assessment.Successful,Is.EqualTo(block));
            Apply(game,1,CommandKind.Defend,response);game=Restore(catalog,game);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(block?2:0));Assert.That(game.View(0).Players[1].AwaitingRespawn,Is.EqualTo(!block));
            Assert.That(game.View(0).Pending?.Optional??false,Is.EqualTo(!block));
        }
        [Test]
        public void SuccessfulDefenseOnRepeatedAttackEndsTheChain()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);DefeatFirst(game);
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:3");Assert.That(game.View(3).DefenseOptions.Single(d=>d.CardId=="arien-13-挑战者").Assessment.Successful,Is.True);
            Apply(game,3,CommandKind.Defend,"arien-13-挑战者");game=Restore(catalog,game);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));Assert.That(game.View(0).RedCrystal,Is.EqualTo(6));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(2));
        }
        [Test]
        public void MinionAsFirstTargetNeverOffersRepeat()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"minion:-1,-3");
            game=Restore(catalog,game);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));Assert.That(game.View(0).RedCrystal,Is.EqualTo(7));
        }
        [Test]
        public void NoRemainingLegalTargetEndsAfterDefeatWithoutAnEmptyChoice()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(7,-8));Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(5,-7));
            DefeatFirst(game);game=Restore(catalog,game);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));Assert.That(game.View(0).Pending,Is.Null);
        }
        [Test]
        public void FinalCrystalVictoryStopsBeforeRepeatAndAwardsOnlyOnce()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);DefeatFirst(game);game=Restore(catalog,game);
            Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(game.View(0).Winner,Is.EqualTo(Team.Blue));Assert.That(game.View(0).Pending,Is.Null);
            string save=game.ExportSave();Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:3")).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(save));
        }
        [TestCase(7,-8,0,false)] [TestCase(6,-6,0,true)] [TestCase(6,-5,0,false)] [TestCase(6,-5,1,true)]
        public void RepeatQueriesReapplyStraightLineAndRange(int x,int y,int ranged,bool legal)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);DefeatFirst(game);var state=new JsonStateCodec().Read(game.ExportSave());
            state.Units.Single(u=>u.Seat==3).Position=new Hex(x,y);state.Players[0].RangedBonus=ranged;
            Assert.That(CombatRules.AttackTargets(catalog,state,0).Contains("hero:3"),Is.EqualTo(legal));
        }
        [Test]
        public void OldBoomerangTargetWindowKeepsItsBytesAndHasNoRepeat()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine18-boomerang-target.json"));var game=LocalGameFactory.Restore(catalog,save);
            Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Thunder));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);game=Restore(catalog,game);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));Assert.That(game.View(0).Pending,Is.Null);
        }
    }
}

#nullable enable
using System;
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
    public sealed partial class OptionalAttackDiscardTests
    {
        [TestCase(Axe,1)]
        [TestCase(Spear,3)]
        public void EmptyHandContinuesAutomaticallyAndOnlySpearUsesTheEarlierDiscards(string card,int range)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card);
            foreach(string id in game.View(0).OwnCards.Where(c=>c.Zone==CardZone.InHand).Select(c=>c.CardId).ToArray()) Apply(game,0,CommandKind.DebugDiscard,id,target:0);
            Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("attack_target")); Assert.That(game.View(0).AttackRange,Is.EqualTo(range));
            Assert.That(game.View(0).OptionalDiscardCards,Is.Empty);
            Assert.That(game.View(null).Events.Any(e=>e.Kind=="OptionalDiscardRequired"),Is.False);
            Assert.That(game.View(null).Events.Single(e=>e.Kind=="OptionalDiscardSkipped").Detail,Is.EqualTo("empty_hand"));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void OptionalWindowOwnChoicesAndDiscardNamesStayPrivateButSourceAndColorArePublic(string card)
        {
            var game=Ready(BattlefieldTests.Catalog(),card); Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).OptionalDiscardCards,Is.EquivalentTo(game.View(0).OwnCards.Where(c=>c.Zone==CardZone.InHand).Select(c=>c.CardId)));
            foreach(int? observer in new int?[] {null,1,2,3})
            {
                Assert.That(game.View(observer).OptionalDiscardCards,Is.Empty); Assert.That(game.View(observer).Pending!.Source,Is.EqualTo(card));
            }
            game.View(0).OptionalDiscardCards.Clear(); Assert.That(game.View(0).OptionalDiscardCards.Count,Is.EqualTo(4));
            Apply(game,0,DiscardCommand,Cost);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardDiscarded" && e.CardId==Cost),Is.EqualTo(1));
            foreach(int? observer in new int?[] {null,1,2,3})
            {
                Assert.That(game.View(observer).Events.Any(e=>e.Kind=="CardDiscarded"),Is.False);
                Assert.That(game.View(observer).Players[0].DiscardColors,Is.EqualTo(new[] {"gold"}));
                Assert.That(game.View(observer).Events.Single(e=>e.Kind=="OptionalDiscardCompleted").CardId,Is.EqualTo(card));
            }
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void WrongSeatInvalidCardsOtherActionsAndRepeatedCostCannotPartiallyChangeTheSave(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card); Apply(game,0,CommandKind.BeginPrimary);
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); string before=game.ExportSave();
            foreach(var invalid in new[] {Cmd(game,1,DiscardCommand,"skip"),Cmd(game,0,DiscardCommand,""),Cmd(game,0,DiscardCommand,card),
                Cmd(game,0,DiscardCommand,"wasp-00-闪耀之刃"),Cmd(game,0,CommandKind.Pass),Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:1"),
                Cmd(game,0,CommandKind.Move,destination:new Hex(5,-8)),Cmd(game,0,CommandKind.DebugDiscard,Cost,target:0),Cmd(game,0,CommandKind.ForcedDiscard,Cost)})
            {
                Assert.That(game.Execute(invalid.ActorSeat,invalid).Accepted,Is.False,invalid.Kind+":"+invalid.Value);
                Assert.That(game.ExportSave(),Is.EqualTo(before));
            }
            var cost=Cmd(game,0,DiscardCommand,Cost);
            Assert.That(game.Execute(1,cost).Code,Is.EqualTo("unauthorized")); Assert.That(game.ExportSave(),Is.EqualTo(before));
            Assert.That(game.Execute(0,cost).Accepted,Is.True); string paid=game.ExportSave();
            game=LocalGameFactory.Restore(catalog,paid); Assert.That(game.Execute(0,cost).Duplicate,Is.True); Assert.That(game.ExportSave(),Is.EqualTo(paid));
            Assert.That(game.Execute(0,Cmd(game,0,DiscardCommand,Cost)).Accepted,Is.False); Assert.That(game.ExportSave(),Is.EqualTo(paid));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardDiscarded"),Is.EqualTo(1));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackRangeDetermined"),Is.EqualTo(1));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void PaidDistanceHasAnExactBoundaryAndUsesRangedInsteadOfSkillRangeBonuses(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card,3); Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,DiscardCommand,Cost);
            // Query fixture: isolate passive attributes without inventing journaled upgrades.
            var state=new JsonStateCodec().Read(game.ExportSave()); var target=state.Units.Single(u=>u.Seat==1); var source=state.Units.Single(u=>u.Seat==0);
            state.Players[0].RangeBonus=99;
            for(int distance=1;distance<=4;distance++)
            {
                target.Position=catalog.Cells.First(c=>!c.Obstacle && c.Position.Distance(source.Position)==distance && state.Units.All(u=>u.Id==target.Id || u.Position!=c.Position)).Position;
                Assert.That(CombatRules.AttackTargets(catalog,state,0).Contains(target.Id),Is.EqualTo(distance<=3),"distance "+distance);
            }
            state.Players[0].RangedBonus=1;
            Assert.That(CombatRules.AttackTargets(catalog,state,0),Does.Contain(target.Id)); Assert.That(CombatRules.CurrentAttackRange(catalog,state),Is.EqualTo(4));
        }
        [TestCase(CardZone.InHand,false)]
        [TestCase(CardZone.Selected,false)]
        [TestCase(CardZone.PlayedResolved,false)]
        [TestCase(CardZone.Discarded,true)]
        public void SpearPreviewCountsOnlyOwnDiscardedZone(CardZone zone,bool extended)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,Spear,3); var state=new JsonStateCodec().Read(game.ExportSave());
            state.Players[0].Cards.Single(c=>c.CardId==Cost).Zone=zone;
            Assert.That(CombatRules.AttackTargets(catalog,state,0).Contains("hero:1"),Is.EqualTo(extended));
        }
        [Test]
        public void SpearDoesNotCountAnotherPlayersDiscardAndMultipleOwnDiscardsAddOnlyTwo()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,Spear,3);
            string enemyCost=game.View(1).OwnCards.First(c=>c.Zone==CardZone.InHand).CardId; Apply(game,0,CommandKind.DebugDiscard,enemyCost,target:1);
            Assert.That(game.View(0).AttackTargets,Does.Not.Contain("hero:1"));
            foreach(string id in game.View(0).OwnCards.Where(c=>c.Zone==CardZone.InHand).Take(2).Select(c=>c.CardId).ToArray()) Apply(game,0,CommandKind.DebugDiscard,id,target:0);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,DiscardCommand,"skip"); Assert.That(game.View(0).AttackRange,Is.EqualTo(3));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void CostRemainsWhenThereAreNoLegalTargetsAndNoAttackOrGoldIsCreated(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card);
            foreach(var enemy in game.View(null).Units.Where(u=>u.Kind=="hero" && u.Team==Team.Red).ToArray())
            {
                var at=catalog.Cells.First(c=>!c.Obstacle && c.Position.Distance(new Hex(6,-8))>=8 && game.View(null).Units.All(u=>u.Position!=c.Position)).Position;
                Apply(game,0,CommandKind.DebugTeleport,enemy.Id,cell:at);
            }
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,DiscardCommand,Cost);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Cost).Zone,Is.EqualTo(CardZone.Discarded));
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==card).Zone,Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(null).Events.Any(e=>e.Kind=="AttackDeclared" || e.Kind=="GoldAwarded"),Is.False);
            Assert.That(game.View(null).Events.Last(e=>e.Kind=="CardEffectStopped").Detail,Is.EqualTo("no_targets"));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void ReflectionAfterPaidDistanceRequiresASeparateDiscardAndRestoresBothWindows(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card,3,reflection:true);
            Apply(game,0,CommandKind.BeginPrimary); game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,0,DiscardCommand,Cost); game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1"); game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,1,CommandKind.Defend,"wasp-10-反射屏障");
            Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("forced_discard")); Assert.That(game.View(0).ForcedDiscardCards,Does.Not.Contain(Cost));
            Assert.That(game.View(0).OptionalDiscardCards,Is.Empty); Assert.That(game.View(0).AttackRange,Is.EqualTo(3));
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); string second=game.View(0).ForcedDiscardCards.First();
            Apply(game,0,CommandKind.ForcedDiscard,second);
            Assert.That(game.View(null).Players[0].DiscardColors.Count,Is.EqualTo(2)); Assert.That(game.View(null).Players[1].DiscardColors,Is.EqualTo(new[] {"green"}));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="ProtectionActivated"),Is.EqualTo(1));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="AttackRangeDetermined"),Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void OldEngineCannotRunNewCostProgramsAndOrdinaryDiscardDoesNotTriggerThem(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card,engineVersion:7); string before=game.ExportSave();
            Assert.That(game.View(0).PrimarySupported,Is.False); Assert.That(game.Execute(0,Cmd(game,0,CommandKind.BeginPrimary)).Code,Is.EqualTo("primary_not_implemented"));
            Assert.That(game.Execute(0,Cmd(game,0,DiscardCommand,"skip")).Accepted,Is.False); Assert.That(game.ExportSave(),Is.EqualTo(before));
            game=LocalGameFactory.Create(catalog,"ordinary-cost-discard",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,wasp,shargatha,arien"); Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            Apply(game,0,CommandKind.DebugDiscard,card,target:0);
            Assert.That(game.View(null).Events.Any(e=>e.Kind.StartsWith("OptionalDiscard",StringComparison.Ordinal)),Is.False);
        }
        [TestCase(Axe,MoveMode.Secondary)]
        [TestCase(Axe,MoveMode.Fast)]
        [TestCase(Spear,MoveMode.Secondary)]
        [TestCase(Spear,MoveMode.Fast)]
        public void MovementUseDoesNotOpenAnAttackCost(string card,MoveMode mode)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card);
            if(mode==MoveMode.Fast) Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-8,8));
            var moves=mode==MoveMode.Fast ? game.View(0).FastMoves : game.View(0).SecondaryMoves;
            Assert.That(moves,Is.Not.Empty); Apply(game,0,CommandKind.Move,cell:moves.First().Destination,mode:mode);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==card).Zone,Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(null).Events.Any(e=>e.Kind.StartsWith("OptionalDiscard",StringComparison.Ordinal) || e.Kind=="AttackDeclared"),Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void SecondaryDefenseIsSixAndDoesNotExecuteAttackText(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"brogan-secondary-defense",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"sabina,brogan,shargatha,arien"); Apply(game,0,CommandKind.DebugEquipCard,card,target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8)); Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));
            foreach(var choice in new[] {(0,"sabina-01-拔枪"),(1,"brogan-06-铜墙铁壁"),(2,"shargatha-06-海妖之歌"),(3,"arien-07-潮水")}) Apply(game,choice.Item1,CommandKind.SelectCard,choice.Item2);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            var defense=game.View(1).DefenseOptions.Single(o=>o.CardId==card);
            Assert.That(defense.Primary,Is.False); Assert.That(defense.Assessment.FinalDefense,Is.EqualTo(6)); Apply(game,1,CommandKind.Defend,card);
            Assert.That(game.View(null).Events.Any(e=>e.Kind.StartsWith("OptionalDiscard",StringComparison.Ordinal)),Is.False);
            Assert.That(game.View(null).RedCrystal,Is.EqualTo(7));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
            catalog.Card(card).Text+="（不同文本）"; Assert.That(CombatRules.HasPrimaryProgram(catalog.Card(card)),Is.False);
        }
        [TestCase(Axe,"防御")]
        [TestCase("brogan-03-奋勇冲锋","先攻")]
        [TestCase(Spear,"范围")]
        [TestCase("brogan-05-勇往直前","移动")]
        public void UpgradeAwardsTheRejectedIconAndDoesNotPutRejectedCardsIntoDiscard(string selected,string bonus)
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"brogan-cost-upgrade",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,wasp,shargatha,arien"); Apply(game,0,CommandKind.DebugSetGold,"28",target:0);
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            if(catalog.Card(selected).Level==3)
                foreach(string color in new[] {"red","green","blue"}) Apply(game,0,CommandKind.ChooseUpgrade,catalog.Cards.First(c=>c.HeroId=="brogan" && c.Color==color && c.Level==2).Id);
            var prior=game.View(0).Players[0].PermanentBonuses; Apply(game,0,CommandKind.ChooseUpgrade,selected);
            Assert.That(game.View(0).OwnUpgradeHistory.Last().Bonus,Is.EqualTo(bonus));
            Assert.That(game.View(0).Players[0].PermanentBonuses[bonus],Is.EqualTo((prior.TryGetValue(bonus,out int count) ? count : 0)+1));
            Assert.That(game.View(0).OwnCards.Any(c=>c.Zone==CardZone.Discarded),Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void PaidTargetsExcludeAlliesMarkersProtectedHeavyAndNonAdjacentImmunity(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card,3); Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,DiscardCommand,Cost);
            var state=new JsonStateCodec().Read(game.ExportSave());
            state.Units.Single(u=>u.Seat==2).Position=new Hex(5,-8);
            var heavy=state.Units.First(u=>u.Team==Team.Red && u.Kind=="heavy"); heavy.Position=new Hex(6,-7);
            var melee=state.Units.First(u=>u.Team==Team.Red && u.Kind=="melee"); melee.Position=new Hex(6,-9);
            var ranged=state.Units.First(u=>u.Team==Team.Red && u.Kind=="ranged"); ranged.Position=new Hex(5,-7);
            state.Units.Add(new UnitState {Id="marker",Kind="marker",Team=Team.Red,Position=new Hex(7,-8)});
            var targets=CombatRules.AttackTargets(catalog,state,0);
            Assert.That(targets,Does.Contain("hero:1").And.Contain(melee.Id).And.Contain(ranged.Id));
            Assert.That(targets,Does.Not.Contain("hero:0").And.Not.Contain("hero:2").And.Not.Contain(heavy.Id).And.Not.Contain("marker"));
            state.Effects.Add(new ActiveEffect {Id="reflection",Kind=EffectKind.NonAdjacentRangedImmunity,ProtectedUnitId="hero:1",Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            Assert.That(CombatRules.AttackTargets(catalog,state,0),Does.Not.Contain("hero:1"));
            state.Units.RemoveAll(u=>u.Id=="marker"); state.Units.Single(u=>u.Seat==1).Position=new Hex(7,-8);
            Assert.That(CombatRules.AttackTargets(catalog,state,0),Does.Contain("hero:1"));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void PaidAttackDefeatsALegalMinionAndAwardsTwoGoldWithoutDefense(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card);
            string minion=game.View(null).Units.First(u=>u.Team==Team.Red && u.Kind=="melee").Id;
            Apply(game,0,CommandKind.DebugTeleport,minion,cell:new Hex(8,-8)); Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,DiscardCommand,Cost);
            Apply(game,0,CommandKind.ChooseAttackTarget,minion);
            Assert.That(game.View(null).Units.Any(u=>u.Id==minion),Is.False); Assert.That(game.View(null).Players[0].Gold,Is.EqualTo(2));
            Assert.That(game.View(null).Events.Any(e=>e.Kind=="DefenseChoiceRequired"),Is.False);
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void LastTurnFinalHandCostThenEmptyReflectionFinishesBeforeRecallAndNextRound(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"brogan-last-cost",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,wasp,shargatha,arien");
            for(int turn=0;turn<3;turn++) Apply(game,0,CommandKind.DebugAdvance,"turn");
            Apply(game,0,CommandKind.DebugEquipCard,card,target:0); Apply(game,0,CommandKind.DebugEquipCard,Cost,target:0);
            Apply(game,0,CommandKind.DebugEquipCard,"wasp-10-反射屏障",target:1);
            for(int other=1;other<4;other++) Apply(game,0,CommandKind.DebugEquipCard,game.View(other).OwnCards.Single(c=>catalog.Card(c.CardId).Color=="gold").CardId,target:other);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8)); Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));
            for(int seat=0;seat<4;seat++) Apply(game,seat,CommandKind.SelectCard,seat==0 ? card : game.View(seat).OwnCards.Single(c=>catalog.Card(c.CardId).Color=="gold").CardId);
            for(int guard=0;guard<8 && game.View(null).ActiveSeat!=0;guard++)
            {
                var view=game.View(null);
                if(view.Pending?.Kind=="initiative") Apply(game,view.Pending.ChooserSeat,CommandKind.ChooseInitiative,target:view.Pending.CandidateSeats.First());
                else Apply(game,view.ActiveSeat!.Value,CommandKind.Pass);
            }
            Assert.That(game.View(null).ActiveSeat,Is.EqualTo(0));
            foreach(string id in game.View(0).OwnCards.Where(c=>c.Zone==CardZone.InHand && c.CardId!=Cost).Select(c=>c.CardId).ToArray()) Apply(game,0,CommandKind.DebugDiscard,id,target:0);
            Apply(game,0,CommandKind.BeginPrimary); game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,0,DiscardCommand,Cost);
            Assert.That(game.View(0).OwnCards.Any(c=>c.Zone==CardZone.InHand),Is.False);
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1"); Apply(game,1,CommandKind.Defend,"wasp-10-反射屏障");
            var state=new JsonStateCodec().Read(game.ExportSave()); Assert.That(state.Phase,Is.EqualTo(Phase.RoundEnd)); Assert.That(state.Effects,Is.Empty);
            Assert.That(state.Events.Count(e=>e.Kind=="ForcedDiscardSkipped"),Is.EqualTo(1));
            Assert.That(state.Events.FindIndex(e=>e.Kind=="OptionalDiscardCompleted"),Is.LessThan(state.Events.FindIndex(e=>e.Kind=="AttackDeclared")));
            Assert.That(state.Events.FindIndex(e=>e.Kind=="ProtectionExpired"),Is.LessThan(state.Events.FindIndex(e=>e.Kind=="RoundEndReached")));
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,0,CommandKind.ResolveRoundEnd); RoundEndTests.FinishUpgrades(game);
            Assert.That(game.View(null).Round,Is.EqualTo(2)); Assert.That(game.View(0).OwnCards.All(c=>c.Zone==CardZone.InHand),Is.True);
            Assert.That(game.View(null).Players.All(p=>p.DiscardColors.Count==0),Is.True);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
    }
}

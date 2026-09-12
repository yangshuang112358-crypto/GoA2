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
    public sealed class ConditionalDefenseTests
    {
        internal const string MeleeBlock="tigerclaw-14-近身格挡", Riposte="tigerclaw-17-近身还击", LeadCharge="sabina-08-带头冲锋";
        internal static GameSession Ready(ContentCatalog catalog,string defense,int engineVersion=GameState.CurrentEngineVersion)
        {
            string hero=catalog.Card(defense).HeroId;
            var game=LocalGameFactory.Create(catalog,"conditional-defense",new[] {"A","B","C","D"},42,true,engineVersion);
            Apply(game,0,CommandKind.DebugPrepare,"wasp,"+hero+",brogan,arien"); Apply(game,0,CommandKind.DebugEquipCard,defense,target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10)); Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));
            string defenderCard=game.View(1).OwnCards.Select(c => catalog.Card(c.CardId)).Where(c => c.PrimaryFamily!="defense" && c.Color!="gold").OrderBy(c => c.Initiative).First().Id;
            foreach(var choice in new[] {(0,"wasp-00-闪耀之刃"),(1,defenderCard),(2,"brogan-06-铜墙铁壁"),(3,"arien-07-潮水")}) Apply(game,choice.Item1,CommandKind.SelectCard,choice.Item2);
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(0)); return game;
        }
        [TestCase(MeleeBlock)]
        [TestCase(Riposte)]
        public void NonRangedBlockRunsAfterTheOriginalAttackTextAndAcceptsAttackerDiscard(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card); BarrierResponseTests.Attack(game);
            Assert.That(game.View(1).DefenseOptions.Any(o => o.CardId==card && o.Block && o.Assessment.Successful), Is.True);
            Apply(game,1,CommandKind.Defend,card);
            var view=game.View(null); Assert.That(view.Pending!.Kind, Is.EqualTo("forced_discard")); Assert.That(view.Pending.ChooserSeat, Is.EqualTo(0));
            Assert.That(view.Effects.Single().SourceCardId, Is.EqualTo("wasp-00-闪耀之刃"));
            Assert.That(view.Events.FindIndex(e => e.Kind=="EffectActivated"), Is.LessThan(view.Events.FindIndex(e => e.Kind=="ForcedDiscardRequired")));
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,0,CommandKind.ForcedDiscard,"wasp-01-电击");
            Assert.That(game.View(null).BlueCrystal, Is.EqualTo(7)); Assert.That(game.View(null).RedCrystal, Is.EqualTo(7));
            Assert.That(game.View(null).Events.Count(e => e.Kind=="AttackResolved"), Is.EqualTo(1));
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution, Is.Null);
        }
        [TestCase(MeleeBlock,false)]
        [TestCase(Riposte,true)]
        public void EmptyHandOnlyDefeatsTheAttackerForRiposte(string card,bool defeated)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card);
            foreach(var hand in game.View(0).OwnCards.Where(c => c.Zone==CardZone.InHand).ToList()) Apply(game,0,CommandKind.DebugDiscard,hand.CardId,target:0);
            BarrierResponseTests.Attack(game); Apply(game,1,CommandKind.Defend,card);
            var view=game.View(null); Assert.That(view.Pending?.Kind, Is.Not.EqualTo("forced_discard"));
            Assert.That(view.Players[0].AwaitingRespawn, Is.EqualTo(defeated)); Assert.That(view.BlueCrystal, Is.EqualTo(defeated ? 6 : 7));
            Assert.That(view.Players[1].Gold, Is.EqualTo(defeated ? 1 : 0)); Assert.That(view.Players[3].Gold, Is.EqualTo(defeated ? 1 : 0));
            Assert.That(view.Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void RiposteCrystalVictoryDoesNotResumeTheAlreadyFinishedMatch()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,Riposte); Apply(game,0,CommandKind.DebugSetCrystal,"1",target:0);
            foreach(var hand in game.View(0).OwnCards.Where(c => c.Zone==CardZone.InHand).ToList()) Apply(game,0,CommandKind.DebugDiscard,hand.CardId,target:0);
            BarrierResponseTests.Attack(game); Apply(game,1,CommandKind.Defend,Riposte);
            var state=new JsonStateCodec().Read(game.ExportSave()); Assert.That(state.Phase, Is.EqualTo(Phase.Finished)); Assert.That(state.Winner, Is.EqualTo(Team.Red));
            Assert.That(state.Execution, Is.Null); Assert.That(state.Pending, Is.Null); Assert.That(state.ActiveSeat, Is.Null);
            Assert.That(state.Events.Last().Kind, Is.EqualTo("MatchWon")); Assert.That(state.Events.Count(e => e.Kind=="AttackResolved"), Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void FriendlyAdjacentMinionEnablesLeadChargeAgainstNonRangedAttack()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,LeadCharge);
            Apply(game,0,CommandKind.DebugTeleport,game.View(null).Units.First(u => u.Team==Team.Red && u.Kind=="melee").Id,cell:new Hex(8,-9));
            BarrierResponseTests.Attack(game);
            Assert.That(game.View(1).DefenseOptions.Any(o => o.CardId==LeadCharge && o.Block && o.Assessment.Successful), Is.True);
            Apply(game,1,CommandKind.Defend,LeadCharge);
            Assert.That(game.View(null).RedCrystal, Is.EqualTo(7)); Assert.That(game.View(null).Events.Any(e => e.Kind=="ForcedDiscardRequired"), Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(MeleeBlock)]
        [TestCase(Riposte)]
        public void NonRangedConditionDependsOnAttackTypeRatherThanDistanceOrNumber(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card); BarrierResponseTests.Attack(game);
            var state=new JsonStateCodec().Read(game.ExportSave());
            foreach(int attack in new[] {-10,0,99})
            {
                state.Execution!.Attack!.FinalAttack=attack;
                Assert.That(CombatRules.DefenseOptions(catalog,state,1).Single(o => o.CardId==card).Assessment.Successful, Is.True);
            }
            state.Units.Single(u => u.Seat==0).Position=new Hex(6,-8); // Distance 2, still a non-ranged query fixture.
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==card), Is.True);
            state.Execution!.Attack!.Ranged=true;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==card), Is.False);
            state.Execution.Attack.Ranged=false; state.Execution.Attack.Unblockable=true;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==card), Is.False);
        }
        [TestCase("melee",Team.Red,1,false,true)]
        [TestCase("ranged",Team.Red,1,false,true)]
        [TestCase("heavy",Team.Red,1,false,true)]
        [TestCase("melee",Team.Red,1,true,true)]
        [TestCase("ranged",Team.Red,1,true,true)]
        [TestCase("heavy",Team.Red,1,true,true)]
        [TestCase("melee",Team.Blue,1,false,false)]
        [TestCase("hero",Team.Red,1,false,false)]
        [TestCase("marker",Team.Red,1,true,false)]
        [TestCase("melee",Team.Red,2,true,false)]
        public void LeadChargeRequiresAnAdjacentFriendlyMinionAndAcceptsBothAttackTypes(string kind,Team team,int distance,bool ranged,bool allowed)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,LeadCharge); BarrierResponseTests.Attack(game);
            var state=new JsonStateCodec().Read(game.ExportSave()); state.Units.RemoveAll(u => u.Kind!="hero");
            state.Units.Add(new UnitState {Id="support",Kind=kind,Team=team,Position=distance==1 ? new Hex(8,-9) : new Hex(7,-8)});
            state.Players[1].RangeBonus=10; state.Players[1].RangedBonus=10; state.Players[1].DefenseBonus=10;
            state.Execution!.Attack!.Ranged=ranged; state.Execution.Attack.FinalAttack=99;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==LeadCharge && o.Block && o.Assessment.Successful), Is.EqualTo(allowed));
            state.Execution.Attack.Unblockable=true;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==LeadCharge), Is.False);
        }
        [TestCase(MeleeBlock)]
        [TestCase(Riposte)]
        public void EvenAdjacentRangedAttackCannotBeBlockedAndExplainsWhyToOnlyTheDefender(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attackCard:MarksmanTests.Headshot,defender:"tigerclaw",equipment:card);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10)); Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));
            BarrierResponseTests.Attack(game); string before=game.ExportSave();
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.Defend,card)).Code, Is.EqualTo("invalid_defense")); Assert.That(game.ExportSave(), Is.EqualTo(before));
            Assert.That(game.View(1).DefenseRestrictions.ContainsKey(card), Is.True);
            Assert.That(game.View(1).DefenseRestrictions[card], Is.EqualTo("requires_non_ranged"));
            foreach(int? viewer in new int?[] {null,0,2,3}) Assert.That(game.View(viewer).DefenseRestrictions, Is.Empty);
        }
        [Test]
        public void MissingFriendlyMinionExplainsTheUnavailableLeadChargeWithoutLeakingItsName()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,LeadCharge); BarrierResponseTests.Attack(game);
            Assert.That(game.View(1).DefenseRestrictions.ContainsKey(LeadCharge), Is.True);
            Assert.That(game.View(1).DefenseRestrictions[LeadCharge], Is.EqualTo("requires_adjacent_friendly_minion"));
            foreach(int? viewer in new int?[] {null,0,2,3}) Assert.That(game.View(viewer).DefenseRestrictions, Is.Empty);
            string before=game.ExportSave(); Assert.That(game.Execute(1,Cmd(game,1,CommandKind.Defend,LeadCharge)).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }
        [TestCase(MeleeBlock)]
        [TestCase(Riposte)]
        public void ForcedDiscardRequiresAnOwnedCardAndIsAtomicAndIdempotent(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card); BarrierResponseTests.Attack(game);
            var defense=Cmd(game,1,CommandKind.Defend,card); Assert.That(game.Execute(1,defense).Accepted, Is.True); Assert.That(game.Execute(1,defense).Duplicate, Is.True);
            string before=game.ExportSave();
            foreach(var command in new[] {Cmd(game,0,CommandKind.ForcedDiscard,""),Cmd(game,0,CommandKind.ForcedDiscard,"skip"),
                Cmd(game,1,CommandKind.ForcedDiscard,"wasp-01-电击"),Cmd(game,0,CommandKind.ForcedDiscard,card),Cmd(game,0,CommandKind.ForcedDiscard,"wasp-00-闪耀之刃"),
                Cmd(game,0,CommandKind.Pass),Cmd(game,0,CommandKind.DeclineDefense),Cmd(game,0,CommandKind.DebugAdvance,"turn"),Cmd(game,0,CommandKind.DebugDiscard,"wasp-01-电击",target:0)})
            {
                Assert.That(game.Execute(command.ActorSeat,command).Accepted, Is.False); Assert.That(game.ExportSave(), Is.EqualTo(before));
            }
            Assert.That(game.View(0).ForcedDiscardCards.Count, Is.EqualTo(4)); Assert.That(game.View(0).Pending!.Source, Is.Empty); Assert.That(game.View(1).Pending!.Source, Is.EqualTo(card));
            foreach(int? viewer in new int?[] {null,1,2,3}) Assert.That(game.View(viewer).ForcedDiscardCards, Is.Empty);
            game=LocalGameFactory.Restore(catalog,before); var discard=Cmd(game,0,CommandKind.ForcedDiscard,"wasp-01-电击");
            Assert.That(game.Execute(0,discard).Accepted, Is.True); string saved=game.ExportSave(); Assert.That(game.Execute(0,discard).Duplicate, Is.True); Assert.That(game.ExportSave(), Is.EqualTo(saved));
            Assert.That(game.View(null).Players[0].AwaitingRespawn, Is.False); Assert.That(game.View(null).Events.Count(e => e.Kind=="AttackResolved"), Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,saved).ExportSave(), Is.EqualTo(saved));
        }
        [Test]
        public void RiposteDefeatKeepsItsCardSourcePrivateWhilePublishingVictimLocationAndRewards()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,Riposte);
            foreach(var hand in game.View(0).OwnCards.Where(c => c.Zone==CardZone.InHand).ToList()) Apply(game,0,CommandKind.DebugDiscard,hand.CardId,target:0);
            BarrierResponseTests.Attack(game); Apply(game,1,CommandKind.Defend,Riposte);
            foreach(int? viewer in new int?[] {null,0,2,3})
            {
                var view=game.View(viewer); Assert.That(view.Events.Any(e => e.CardId==Riposte || e.Kind=="HeroDefeatSource"), Is.False);
                var defeat=view.Events.Single(e => e.Kind=="HeroDefeated"); Assert.That(defeat.Seat, Is.EqualTo(0)); Assert.That(defeat.CardId, Is.Null);
                Assert.That(defeat.Detail, Is.EqualTo("by:1")); Assert.That(defeat.From, Is.EqualTo(new Hex(7,-10)));
            }
            var owner=game.View(1); Assert.That(owner.Events.Single(e => e.Kind=="HeroDefeatSource").CardId, Is.EqualTo(Riposte));
            Assert.That(owner.Events.FindIndex(e => e.Kind=="EffectActivated"), Is.LessThan(owner.Events.FindIndex(e => e.Kind=="HeroDefeated")));
            Assert.That(owner.Events.Count(e => e.Kind=="AttackCalculated"), Is.EqualTo(1)); Assert.That(owner.Events.Count(e => e.Kind=="DefenseChoiceRequired"), Is.EqualTo(1));
            Assert.That(owner.Effects.Single().SourceCardId, Is.EqualTo("wasp-00-闪耀之刃")); Assert.That(owner.EffectAreas.Values.Single(), Is.Empty);
        }
        [TestCase(MeleeBlock)]
        [TestCase(Riposte)]
        [TestCase(LeadCharge)]
        public void PreviousEngineAndChangedFormalTextDoNotAcquireNewResponses(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card,6); BarrierResponseTests.Attack(game);
            Assert.That(game.View(1).UnimplementedDefenseCards, Does.Contain(card)); string before=game.ExportSave();
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.Defend,card)).Code, Is.EqualTo("response_not_implemented")); Assert.That(game.ExportSave(), Is.EqualTo(before));
            Assert.That(LocalGameFactory.Restore(catalog,before).ExportSave(), Is.EqualTo(before));
            catalog.Card(card).Text+="（另有条件）"; Assert.That(CombatRules.HasDefenseProgram(catalog.Card(card)), Is.False);
        }
        [TestCase(MeleeBlock,MoveMode.Secondary)]
        [TestCase(MeleeBlock,MoveMode.Fast)]
        [TestCase(Riposte,MoveMode.Secondary)]
        [TestCase(Riposte,MoveMode.Fast)]
        [TestCase(LeadCharge,MoveMode.Secondary)]
        [TestCase(LeadCharge,MoveMode.Fast)]
        public void MovementUseDoesNotTriggerAnyDefenseResponse(string card,MoveMode mode)
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"defense-move",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,catalog.Card(card).HeroId+",wasp,brogan,arien"); Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            foreach(var choice in new[] {(0,card),(1,"wasp-07-抵挡屏障"),(2,"brogan-06-铜墙铁壁"),(3,"arien-07-潮水")}) Apply(game,choice.Item1,CommandKind.SelectCard,choice.Item2);
            AdvanceUntilSeat(game,0);
            string before=game.ExportSave(); Assert.That(game.Execute(0,Cmd(game,0,CommandKind.BeginPrimary)).Code, Is.EqualTo("primary_not_implemented")); Assert.That(game.ExportSave(), Is.EqualTo(before));
            var moves=mode==MoveMode.Fast ? game.View(0).FastMoves : game.View(0).SecondaryMoves;
            Assert.That(moves, Is.Not.Empty); Apply(game,0,CommandKind.Move,cell:moves.First().Destination,mode:mode);
            Assert.That(game.View(null).Effects, Is.Empty); Assert.That(game.View(null).Events.Any(e => e.Kind=="ForcedDiscardRequired" || e.Kind=="HeroDefeatSource"), Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(MeleeBlock)]
        [TestCase(Riposte)]
        [TestCase(LeadCharge)]
        public void OrdinaryDiscardDoesNotRunDefenseTextAndSpentCardCannotDefend(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card); Apply(game,0,CommandKind.DebugDiscard,card,target:1);
            BarrierResponseTests.Attack(game); string before=game.ExportSave(); Assert.That(game.Execute(1,Cmd(game,1,CommandKind.Defend,card)).Code, Is.EqualTo("invalid_defense"));
            Assert.That(game.ExportSave(), Is.EqualTo(before)); Assert.That(game.View(1).DefenseRestrictions.ContainsKey(card), Is.False);
            Apply(game,1,CommandKind.Defend,game.View(1).DefenseOptions.First(o => o.Assessment.Successful).CardId);
            Assert.That(game.View(null).BlueCrystal, Is.EqualTo(7)); Assert.That(game.View(null).Events.Any(e => e.Kind=="ForcedDiscardRequired" || e.Kind=="HeroDefeatSource"), Is.False);
        }
        [TestCase(MeleeBlock,"攻击")]
        [TestCase("tigerclaw-15-侧步","防御")]
        [TestCase(Riposte,"远程")]
        [TestCase("tigerclaw-16-暗影步","攻击")]
        [TestCase(LeadCharge,"先攻")]
        [TestCase("sabina-09-战略控制","攻击")]
        public void UpgradeRecordsTheUnselectedCandidateAndItsPassive(string card,string bonus)
        {
            var catalog=BattlefieldTests.Catalog(); string hero=catalog.Card(card).HeroId;
            var game=LocalGameFactory.Create(catalog,"conditional-upgrade",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,hero+",wasp,brogan,arien"); Apply(game,0,CommandKind.DebugSetGold,"28",target:0);
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            if(catalog.Card(card).Level==3)
                foreach(string color in new[] {"red","green","blue"}) Apply(game,0,CommandKind.ChooseUpgrade,catalog.Cards.First(c => c.HeroId==hero && c.Color==color && c.Level==2).Id);
            var prior=game.View(0).Players[0].PermanentBonuses; Apply(game,0,CommandKind.ChooseUpgrade,card);
            var record=game.View(0).OwnUpgradeHistory.Last(); Assert.That(record.SelectedCardId, Is.EqualTo(card)); Assert.That(record.Bonus, Is.EqualTo(bonus));
            Assert.That(catalog.Card(record.RejectedCardId).Passive, Is.EqualTo(bonus));
            Assert.That(game.View(0).Players[0].PermanentBonuses[bonus], Is.EqualTo((prior.TryGetValue(bonus,out int count) ? count : 0)+1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void FourthTurnFinalAttackCanBeCounterDefeatedThenReachUpgradesAndRespawnNextRound(bool voluntary)
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"last-turn-riposte",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"shargatha,tigerclaw,brogan,arien");
            for(int turn=1;turn<4;turn++) Apply(game,0,CommandKind.DebugAdvance,"turn");
            Apply(game,0,CommandKind.DebugEquipCard,"shargatha-01-劈砍",target:0); Apply(game,0,CommandKind.DebugEquipCard,Riposte,target:1);
            if(voluntary) Apply(game,0,CommandKind.DebugEquipCard,catalog.Cards.Single(c=>c.HeroId=="shargatha" && c.Color=="silver").Id,target:0);
            for(int seat=1;seat<4;seat++) Apply(game,0,CommandKind.DebugEquipCard,game.View(seat).OwnCards.Single(c => catalog.Card(c.CardId).Color=="gold").CardId,target:seat);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10)); Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));
            Apply(game,0,CommandKind.SelectCard,"shargatha-01-劈砍");
            for(int seat=1;seat<4;seat++) Apply(game,seat,CommandKind.SelectCard,game.View(seat).OwnCards.Single(c => catalog.Card(c.CardId).Color=="gold").CardId);
            AdvanceUntilSeat(game,0);
            Assert.That(game.View(null).Players.Skip(1).All(p => p.Revealed.All(c => c.Zone==CardZone.PlayedResolved)), Is.True);
            if(!voluntary) foreach(var hand in game.View(0).OwnCards.Where(c => c.Zone==CardZone.InHand).ToList()) Apply(game,0,CommandKind.DebugDiscard,hand.CardId,target:0);
            BarrierResponseTests.Attack(game); Apply(game,1,CommandKind.Defend,Riposte);
            if(voluntary) { game=LocalGameFactory.Restore(catalog,game.ExportSave());Apply(game,0,CommandKind.DeclineRetaliationDiscard); }
            var view=game.View(null); Assert.That(view.Phase, Is.EqualTo(Phase.RoundEnd)); Assert.That(view.Turn, Is.EqualTo(4)); Assert.That(view.Players[0].AwaitingRespawn, Is.True);
            Assert.That(view.Events.FindLastIndex(e => e.Kind=="HeroDefeated"), Is.LessThan(view.Events.FindLastIndex(e => e.Kind=="CardResolved")));
            Assert.That(view.Events.FindLastIndex(e => e.Kind=="CardResolved"), Is.LessThan(view.Events.FindLastIndex(e => e.Kind=="RoundEndReached")));
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(null).UpgradingSeats, Is.EquivalentTo(new[] {1,3}));
            foreach(int seat in game.View(null).UpgradingSeats.ToArray()) Apply(game,seat,CommandKind.ChooseUpgrade,game.View(seat).UpgradeOptions.First().CardId);
            Assert.That(game.View(null).Round, Is.EqualTo(2)); Assert.That(game.View(null).Phase, Is.EqualTo(Phase.Planning));
            for(int seat=0;seat<4;seat++) Apply(game,seat,CommandKind.SelectCard,game.View(seat).OwnCards.Single(c => catalog.Card(c.CardId).Color=="gold").CardId);
            AdvanceUntilSeat(game,0);
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("hero_respawn")); Assert.That(game.View(null).Pending!.ChooserSeat, Is.EqualTo(0));
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,0,CommandKind.RespawnHero,cell:game.View(0).RespawnCells.First());
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(0)); Assert.That(game.View(null).Players[0].AwaitingRespawn, Is.False); Assert.That(game.View(null).BlueCrystal, Is.EqualTo(6));
            Assert.That(game.View(null).Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedUnresolved)); Apply(game,0,CommandKind.Pass);
            Assert.That(game.View(null).Events.Count(e => e.Kind=="HeroDefeated"), Is.EqualTo(1)); Assert.That(game.View(null).Events.Count(e => e.Kind=="HeroRespawned"), Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        private static void AdvanceUntilSeat(GameSession game,int seat)
        {
            for(int guard=0;guard<8 && game.View(null).ActiveSeat!=seat;guard++)
            {
                var view=game.View(null);
                if(view.Pending?.Kind=="initiative") Apply(game,view.Pending.ChooserSeat,CommandKind.ChooseInitiative,target:view.Pending.CandidateSeats.Contains(seat) ? seat : view.Pending.CandidateSeats.First());
                else Apply(game,view.ActiveSeat!.Value,CommandKind.Pass);
            }
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(seat));
        }
    }
}

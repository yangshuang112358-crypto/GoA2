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
    public sealed class BarrierResponseTests
    {
        internal const string Deflection = "wasp-08-偏转屏障", Reflection = "wasp-10-反射屏障";
        internal static void Attack(GameSession game)
        {
            Apply(game,0,CommandKind.BeginPrimary);
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
        }
        [TestCase(Deflection)]
        [TestCase(Reflection)]
        public void BarrierIsAvailableOnlyForNonAdjacentBlockableRangedAttack(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,equipment:card); Attack(game);
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==card && o.Block && o.Assessment.Successful), Is.True);
            state.Execution!.Attack!.FinalAttack=-2;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==card && o.Assessment.Successful), Is.True);
            state.Execution.Attack.Ranged=false;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==card), Is.False);
            state.Execution.Attack.Ranged=true; state.Execution.Attack.Unblockable=true;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==card), Is.False);
            state.Execution.Attack.Unblockable=false;
            state.Units.Single(u => u.Seat==0).Position=state.Units.Single(u => u.Seat==1).Position.Neighbors().First();
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId==card), Is.False);
        }
        [TestCase(Deflection)]
        [TestCase(Reflection)]
        public void SuccessfulBarrierWaitsForAttackerAndRestoresWithoutRepeatingAttack(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,equipment:card); Attack(game);
            var defense=Cmd(game,1,CommandKind.Defend,card);
            Assert.That(game.Execute(1,defense).Accepted, Is.True);
            Assert.That(game.Execute(1,defense).Duplicate, Is.True);
            var view=game.View(0);
            Assert.That(view.Pending!.Kind, Is.EqualTo("forced_discard"));
            Assert.That(view.Pending.ChooserSeat, Is.EqualTo(0)); Assert.That(view.Pending.Optional, Is.False);
            Assert.That(view.Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedUnresolved));
            Assert.That(view.Events.Count(e => e.Kind=="AttackResolved"), Is.EqualTo(1));
            Assert.That(view.Events.FindIndex(e => e.Kind=="AttackResolved"), Is.LessThan(view.Events.FindIndex(e => e.Kind=="ForcedDiscardRequired")));
            Assert.That(view.Pending.Source, Is.Empty);
            Assert.That(game.View(1).Pending!.Source, Is.EqualTo(card));
            Assert.That(view.Events.Any(e => e.CardId==card), Is.False);
            string saved=game.ExportSave(); game=LocalGameFactory.Restore(catalog,saved);
            Assert.That(game.ExportSave(), Is.EqualTo(saved));
            var discard=Cmd(game,0,CommandKind.ForcedDiscard,"sabina-00-近身射击");
            Assert.That(game.Execute(0,discard).Accepted, Is.True);
            Assert.That(game.Execute(0,discard).Duplicate, Is.True);
            Assert.That(game.View(0).OwnCards.Single(c => c.CardId==discard.Value).Zone, Is.EqualTo(CardZone.Discarded));
            Assert.That(game.View(null).Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(null).Events.Count(e => e.Kind=="AttackResolved"), Is.EqualTo(1));
            Assert.That(game.View(null).Events.Any(e => e.CardId==card || e.Kind=="CardDiscarded"), Is.False);
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution, Is.Null);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Deflection)]
        [TestCase(Reflection)]
        public void OnlyAttackerReceivesCandidatesAndNoOneCanSkipOrAlterThePendingCard(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,equipment:card); Attack(game); Apply(game,1,CommandKind.Defend,card);
            Assert.That(game.View(0).ForcedDiscardCards, Is.EquivalentTo(game.View(0).OwnCards.Where(c => c.Zone==CardZone.InHand).Select(c => c.CardId)));
            foreach(int? seat in new int?[] {null,1,2,3}) Assert.That(game.View(seat).ForcedDiscardCards, Is.Empty);
            string before=game.ExportSave();
            foreach(var command in new[] {
                Cmd(game,1,CommandKind.ForcedDiscard,"sabina-00-近身射击"),
                Cmd(game,0,CommandKind.ForcedDiscard,"wasp-00-闪耀之刃"),
                Cmd(game,0,CommandKind.ForcedDiscard,"sabina-01-拔枪"),
                Cmd(game,0,CommandKind.ForcedDiscard,""), Cmd(game,0,CommandKind.ForcedDiscard,"skip"),
                Cmd(game,0,CommandKind.Pass), Cmd(game,0,CommandKind.DebugAdvance,"turn"),
                Cmd(game,0,CommandKind.DebugDiscard,"sabina-00-近身射击",target:0),
                Cmd(game,0,CommandKind.DebugTeleport,"hero:1",destination:new Hex(8,-10)),
                Cmd(game,0,CommandKind.BeginPrimary), Cmd(game,1,CommandKind.DeclineDefense) })
            {
                Assert.That(game.Execute(command.ActorSeat,command).Accepted, Is.False,command.Kind+":"+command.Value);
                Assert.That(game.ExportSave(), Is.EqualTo(before));
            }
            var detached=game.View(0); detached.ForcedDiscardCards.Clear(); detached.Pending!.ChooserSeat=3;
            Assert.That(game.View(0).ForcedDiscardCards.Count, Is.EqualTo(4)); Assert.That(game.View(0).Pending!.ChooserSeat, Is.EqualTo(0));
        }
        [TestCase(Deflection)]
        [TestCase(Reflection)]
        public void OlderEngineDoesNotGainUnrecordedResponsePrograms(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,equipment:card,engineVersion:3); Attack(game);
            Assert.That(game.View(1).UnimplementedDefenseCards, Does.Contain(card));
            string before=game.ExportSave(); Assert.That(game.Execute(1,Cmd(game,1,CommandKind.Defend,card)).Code, Is.EqualTo("response_not_implemented"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Assert.That(LocalGameFactory.Restore(catalog,before).ExportSave(), Is.EqualTo(before));
            catalog.Card(card).Text+="（其他效果）";
            Assert.That(CombatRules.HasDefenseProgram(catalog.Card(card)), Is.False);
        }
        [Test]
        public void ReflectionProtectsAgainstAllNonAdjacentHeroRangedSourcesWithoutSuppressingOtherActionTypes()
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,equipment:Reflection); Attack(game);
            Apply(game,1,CommandKind.Defend,Reflection); Apply(game,0,CommandKind.ForcedDiscard,"sabina-00-近身射击");
            var state=new JsonStateCodec().Read(game.ExportSave()); var effect=state.Effects.Single();
            Assert.That(effect.Kind, Is.EqualTo(EffectKind.NonAdjacentRangedImmunity)); Assert.That(effect.ProtectedUnitId, Is.EqualTo("hero:1"));
            Assert.That(effect.SourcePrivateTo, Is.EqualTo(1)); Assert.That(game.View(1).Effects.Single().SourceCardId, Is.EqualTo(Reflection));
            foreach(int? viewer in new int?[] {null,0,2,3})
            {
                Assert.That(game.View(viewer).Effects.Single().SourceCardId, Is.Empty);
                Assert.That(game.View(viewer).EffectAreas[effect.Id], Is.Empty);
                Assert.That(game.View(viewer).Events.Any(e => e.CardId==Reflection), Is.False);
            }
            var target=state.Units.Single(u => u.Seat==1); var source=state.Units.Single(u => u.Seat==0);
            Assert.That(EffectRules.CanBeAttacked(state,source,target,true), Is.False);
            Assert.That(EffectRules.CanBeAttacked(state,source,target,false), Is.True);
            source=state.Units.Single(u => u.Seat==2);
            Assert.That(EffectRules.CanBeAttacked(state,source,target,true), Is.False);
            source.Position=target.Position.Neighbors().First();
            Assert.That(EffectRules.CanBeAttacked(state,source,target,true), Is.True);
            Assert.That(EffectRules.SkillRestriction(catalog,state,1,catalog.Card("wasp-06-静电封锁")), Is.Empty);
            Assert.That(EffectRules.CanMoveAcross(catalog,state,target,target.Position,target.Position.Neighbors().First()), Is.True);
            Assert.That(EffectRules.CancellableAdjacentSkills(catalog,state,2), Is.Empty);
            Apply(game,0,CommandKind.DebugAdvance,"turn");
            Assert.That(game.View(null).Effects, Is.Empty); Assert.That(game.View(null).Events.Count(e => e.Kind=="ProtectionExpired"), Is.EqualTo(1));
            Assert.That(game.View(0).Events.Any(e => e.CardId==Reflection), Is.False);
            state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(EffectRules.CanBeAttacked(state,state.Units.Single(u => u.Seat==0),state.Units.Single(u => u.Seat==1),true), Is.True);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void ASecondRangedAttackerCannotSelectTheProtectedHero()
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"two-attackers",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"sabina,wasp,shargatha,arien");
            Apply(game,0,CommandKind.DebugEquipCard,Reflection,target:1);
            Apply(game,0,CommandKind.DebugEquipCard,"shargatha-02-快速突刺",target:2);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(4,-8));
            foreach(var item in new[] {(0,"sabina-01-拔枪"),(1,"wasp-01-电击"),(2,"shargatha-02-快速突刺"),(3,"arien-07-潮水")}) Apply(game,item.Item1,CommandKind.SelectCard,item.Item2);
            Apply(game,0,CommandKind.ChooseInitiative,target:0); Attack(game);
            Apply(game,1,CommandKind.Defend,Reflection); Apply(game,0,CommandKind.ForcedDiscard,"sabina-00-近身射击");
            Apply(game,1,CommandKind.Pass); Assert.That(game.View(null).ActiveSeat, Is.EqualTo(2));
            Assert.That(game.View(2).AttackTargets, Does.Not.Contain("hero:1"));
            Assert.That(game.View(2).AttackTargets, Does.Contain("hero:3"));
            Apply(game,2,CommandKind.BeginPrimary);
            string before=game.ExportSave(); Assert.That(game.Execute(2,Cmd(game,2,CommandKind.ChooseAttackTarget,"hero:1")).Code, Is.EqualTo("invalid_attack_target"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Assert.That(LocalGameFactory.Restore(catalog,before).ExportSave(), Is.EqualTo(before));
            Apply(game,2,CommandKind.ChooseAttackTarget,"hero:3"); Assert.That(game.View(null).Pending!.ChooserSeat, Is.EqualTo(3));
        }
        [TestCase(Deflection,false)]
        [TestCase(Reflection,false)]
        [TestCase(Deflection,true)]
        [TestCase(Reflection,true)]
        public void LastTurnCounterCompletesBeforeRoundEndAndTimedEffectExpiry(string card,bool lastAction)
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"turn-four",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"sabina,wasp,brogan,arien");
            for(int n=0;n<3;n++) Apply(game,0,CommandKind.DebugAdvance,"turn");
            Apply(game,0,CommandKind.DebugEquipCard,"sabina-01-拔枪",target:0);
            Apply(game,0,CommandKind.DebugEquipCard,"wasp-01-电击",target:1);
            Apply(game,0,CommandKind.DebugEquipCard,card,target:1);
            if(lastAction)
                for(int other=1;other<4;other++) Apply(game,0,CommandKind.DebugEquipCard,game.View(other).OwnCards.Single(c => catalog.Card(c.CardId).Color=="gold").CardId,target:other);
            Apply(game,0,CommandKind.DebugSetCoin,"blue");
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            for(int seat=0;seat<4;seat++)
            {
                string selected=seat==0 ? "sabina-01-拔枪" : lastAction ? game.View(seat).OwnCards.Single(c => catalog.Card(c.CardId).Color=="gold").CardId : seat==1 ? "wasp-01-电击" : game.View(seat).OwnCards.Where(c => c.Zone==CardZone.InHand).Select(c => catalog.Card(c.CardId)).OrderBy(c => c.Initiative).First().Id;
                Apply(game,seat,CommandKind.SelectCard,selected);
            }
            for(int guard=0;guard<8 && game.View(null).ActiveSeat!=0;guard++)
            {
                var view=game.View(null);
                if(view.Pending?.Kind=="initiative") Apply(game,view.Pending.ChooserSeat,CommandKind.ChooseInitiative,target:view.Pending.CandidateSeats.First());
                else Apply(game,view.ActiveSeat!.Value,CommandKind.Pass);
            }
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(0));
            if(lastAction) Assert.That(game.View(null).Players.Skip(1).All(p => p.Revealed.All(c => c.Zone==CardZone.PlayedResolved)), Is.True);
            Attack(game); Apply(game,1,CommandKind.Defend,card);
            Assert.That(game.View(null).Turn, Is.EqualTo(4)); Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("forced_discard"));
            Assert.That(game.View(null).Events.Any(e => e.Kind=="RoundEndReached"), Is.False);
            string before=game.ExportSave(); Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DebugAdvance,"round")).Accepted, Is.False);
            Assert.That(game.ExportSave(), Is.EqualTo(before)); game=LocalGameFactory.Restore(catalog,before);
            Apply(game,0,CommandKind.ForcedDiscard,game.View(0).ForcedDiscardCards.First());
            if(!lastAction) Apply(game,0,CommandKind.DebugAdvance,"turn");
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.Phase, Is.EqualTo(Phase.RoundEnd)); Assert.That(state.Effects, Is.Empty);
            Assert.That(state.Events.FindIndex(e => e.Kind=="DefenseResponseCompleted"), Is.LessThan(state.Events.FindIndex(e => e.Kind=="RoundEndReached")));
            if (card==Reflection) Assert.That(state.Events.FindIndex(e => e.Kind=="EffectActivated"), Is.LessThan(state.Events.FindIndex(e => e.Kind=="EffectExpired")));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Deflection,MoveMode.Secondary)]
        [TestCase(Reflection,MoveMode.Secondary)]
        [TestCase(Deflection,MoveMode.Fast)]
        [TestCase(Reflection,MoveMode.Fast)]
        public void PlayingBarrierForMovementDoesNotRunItsDefensiveText(string card,MoveMode mode)
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            for(int seat=0;seat<4;seat++)
            {
                string selected=seat==0 ? card : game.View(seat).OwnCards.Select(c => catalog.Card(c.CardId)).OrderBy(c => c.Initiative).First().Id;
                Apply(game,seat,CommandKind.SelectCard,selected);
            }
            for(int guard=0;guard<8 && game.View(null).ActiveSeat!=0;guard++)
            {
                var view=game.View(null);
                if(view.Pending?.Kind=="initiative") Apply(game,view.Pending.ChooserSeat,CommandKind.ChooseInitiative,target:view.Pending.CandidateSeats.Contains(0) ? 0 : view.Pending.CandidateSeats.First());
                else Apply(game,view.ActiveSeat!.Value,CommandKind.Pass);
            }
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(0));
            string before=game.ExportSave(); Assert.That(game.Execute(0,Cmd(game,0,CommandKind.BeginPrimary)).Code, Is.EqualTo("primary_not_implemented"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            var moves=mode==MoveMode.Secondary ? game.View(0).SecondaryMoves : game.View(0).FastMoves;
            Assert.That(moves, Is.Not.Empty); Apply(game,0,CommandKind.Move,cell:moves.First().Destination,mode:mode);
            Assert.That(game.View(null).Effects, Is.Empty); Assert.That(game.View(null).Events.Any(e => e.Kind=="ForcedDiscardRequired"), Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Deflection,0,1,0)]
        [TestCase("wasp-09-意念操控",1,0,0)]
        [TestCase(Reflection,0,0,1)]
        [TestCase("wasp-11-心灵控制",1,0,0)]
        public void BarrierUpgradeGrantsTheRejectedIconWithItsRecordedSource(string selected,int initiative,int attack,int range)
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog);
            Apply(game,0,CommandKind.DebugSetGold,"28",target:0); Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            if(catalog.Card(selected).Level==3)
                foreach(string previous in new[] {"wasp-02-回旋镖",Deflection,"wasp-14-动力助推"}) Apply(game,0,CommandKind.ChooseUpgrade,previous);
            var before=new JsonStateCodec().Read(game.ExportSave()).Players[0]; Apply(game,0,CommandKind.ChooseUpgrade,selected);
            var after=new JsonStateCodec().Read(game.ExportSave()).Players[0];
            Assert.That(after.InitiativeBonus-before.InitiativeBonus, Is.EqualTo(initiative));
            Assert.That(after.AttackBonus-before.AttackBonus, Is.EqualTo(attack)); Assert.That(after.RangeBonus-before.RangeBonus, Is.EqualTo(range));
            var record=after.UpgradeHistory.Last(); Assert.That(record.SelectedCardId, Is.EqualTo(selected));
            Assert.That(record.Bonus, Is.EqualTo(initiative==1 ? "先攻" : attack==1 ? "攻击" : "范围"));
            Assert.That(record.RejectedCardId, Is.Not.EqualTo(selected));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Deflection,0)]
        [TestCase(Reflection,1)]
        public void EmptyAttackerHandAutomaticallyCompletesCounterAndStillAppliesFollowingImmunity(string card,int effects)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,equipment:card);
            foreach(var hand in game.View(0).OwnCards.Where(c => c.Zone==CardZone.InHand).ToList()) Apply(game,0,CommandKind.DebugDiscard,hand.CardId,target:0);
            Attack(game); Apply(game,1,CommandKind.Defend,card);
            Assert.That(game.View(null).Pending?.Kind, Is.Not.EqualTo("forced_discard"));
            Assert.That(game.View(null).Effects.Count, Is.EqualTo(effects));
            Assert.That(game.View(null).Events.Count(e => e.Kind=="ForcedDiscardSkipped"), Is.EqualTo(1));
            Assert.That(game.View(null).Players[0].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
    }
}

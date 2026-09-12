using System;
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
    public sealed class WarDrumTests
    {
        internal const string Drum="brogan-08-战鼓",Axe="brogan-02-投掷飞斧",Dodge="tigerclaw-18-躲闪";
        internal static CommandKind TargetChoice=>Enum.Parse<CommandKind>("ChooseEffectTarget");
        internal static GameSession Setup(ContentCatalog catalog,string card=Drum,int engine=GameState.CurrentEngineVersion)
        {
            var game=LocalGameFactory.Create(catalog,"war-drum",new[]{"A","B","C","D"},42,true,engine);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,sabina,tigerclaw,arien");Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            Apply(game,0,CommandKind.DebugEquipCard,Axe,target:0);Apply(game,0,CommandKind.DebugEquipCard,Dodge,target:2);
            Apply(game,0,CommandKind.DebugSetCoin,"blue");
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(6,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-9));Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(8,-10));
            Apply(game,0,CommandKind.DebugDiscard,Axe,target:0);Apply(game,0,CommandKind.DebugDiscard,Dodge,target:2);
            return game;
        }
        internal static void Select(GameSession game,string card=Drum,bool silence=false)
        {
            string[] cards={card,"sabina-00-近身射击","tigerclaw-02-偷袭",silence?"arien-06-打断施法":"arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            while(game.View(null).ActiveSeat!=0)
            {
                var view=game.View(null);
                if(view.Phase==Phase.InitiativeChoice)Apply(game,view.Pending!.ChooserSeat,CommandKind.ChooseInitiative,target:view.Pending.CandidateSeats.Contains(0)?0:view.Pending.CandidateSeats.First());
                else {Assert.That(view.ActiveSeat,Is.Not.Null);Apply(game,view.ActiveSeat!.Value,silence && view.ActiveSeat==3 ? CommandKind.BeginPrimary : CommandKind.Pass);}
            }
        }
        private static GameSession Restore(ContentCatalog catalog,GameSession game)
        {string saved=game.ExportSave();var result=LocalGameFactory.Restore(catalog,saved);Assert.That(result.ExportSave(),Is.EqualTo(saved));return result;}
        [Test]
        public void ExactSkillDataAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Drum);
            Assert.That(card.PrimaryFamily,Is.EqualTo("skill"));Assert.That(card.Subtype,Is.EqualTo("远程"));Assert.That(card.SubtypeValue,Is.EqualTo(3));
            Assert.That(card.Initiative,Is.EqualTo(5));Assert.That(card.SecondaryMovement,Is.EqualTo(2));Assert.That(card.SecondaryDefense,Is.EqualTo(5));
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,14),Is.False);
        }
        [TestCase(0,false)] [TestCase(0,true)] [TestCase(2,false)] [TestCase(2,true)]
        public void BeneficiaryChoosesTheirOwnCardOrSkipsAndTheSourceActionEnds(int beneficiary,bool skip)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Select(game);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("effect_target"));Assert.That(game.View(0).Pending!.CandidateUnits,Is.EquivalentTo(new[]{"hero:0","hero:2"}));
            game=Restore(catalog,game);Apply(game,0,TargetChoice,"hero:"+beneficiary);game=Restore(catalog,game);
            Assert.That(game.View(null).ActiveSeat,Is.EqualTo(0));Assert.That(game.View(null).Pending?.Kind,Is.EqualTo("recover_discard"));
            Assert.That(game.View(null).Pending!.ChooserSeat,Is.EqualTo(beneficiary));
            string card=beneficiary==0?Axe:Dodge;
            Assert.That(game.View(beneficiary).RecoverableCards,Is.EqualTo(new[]{card}));
            foreach(int other in Enumerable.Range(0,4).Where(s=>s!=beneficiary))Assert.That(game.View(other).RecoverableCards,Is.Empty);
            var command=Cmd(game,beneficiary,CommandKind.ChooseRecoveredCard,skip?"skip":card);
            Assert.That(game.Execute(beneficiary,command).Accepted,Is.True);game=Restore(catalog,game);string after=game.ExportSave();
            Assert.That(game.Execute(beneficiary,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.View(beneficiary).OwnCards.Single(c=>c.CardId==card).Zone,Is.EqualTo(skip?CardZone.Discarded:CardZone.InHand));
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Drum).Zone,Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="CardResolved" && e.Seat==0),Is.EqualTo(1));
            if(!skip){Assert.That(game.View(null).Events.Any(e=>e.Kind=="CardRecovered"),Is.False);Assert.That(game.View(beneficiary).Events.Count(e=>e.Kind=="CardRecovered"),Is.EqualTo(1));}
        }
        [Test]
        public void TargetAndRecoveryCommandsRejectWrongSeatsAndWrongCardZonesAtomically()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Select(game);Apply(game,0,CommandKind.BeginPrimary);string before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,2,TargetChoice,"hero:2"),Cmd(game,0,TargetChoice,"hero:1"),Cmd(game,0,TargetChoice,"minion:-1,-3"),Cmd(game,0,CommandKind.ChooseRecoveredCard,Axe)})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            Apply(game,0,TargetChoice,"hero:2");before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,0,CommandKind.ChooseRecoveredCard,Dodge),Cmd(game,2,CommandKind.ChooseRecoveredCard,Axe),Cmd(game,2,CommandKind.ChooseRecoveredCard,"tigerclaw-02-偷袭"),Cmd(game,2,CommandKind.ChooseRecoveredCard,"tigerclaw-07-伺机待发")})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
        }
        [Test]
        public void RecoveredDodgeIsAvailableForTheNextTurnDrawAttack()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Select(game);Apply(game,0,CommandKind.BeginPrimary);
            Apply(game,0,TargetChoice,"hero:2");Apply(game,2,CommandKind.ChooseRecoveredCard,Dodge);
            Apply(game,0,CommandKind.DebugAdvance,"turn");
            string[] cards={"brogan-13-冲拳","sabina-01-拔枪","tigerclaw-07-伺机待发","arien-01-汹涌"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            while(game.View(null).ActiveSeat!=1)
            {
                var view=game.View(null);
                if(view.Phase==Phase.InitiativeChoice)Apply(game,view.Pending!.ChooserSeat,CommandKind.ChooseInitiative,target:1);
                else Apply(game,view.ActiveSeat!.Value,CommandKind.Pass);
            }
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:2");
            Assert.That(game.View(2).DefenseOptions.Any(o=>o.CardId==Dodge && o.Block),Is.True);Apply(game,2,CommandKind.Defend,Dodge);
            Assert.That(game.View(2).Players[2].AwaitingRespawn,Is.False);Assert.That(game.View(2).Players[0].Gold,Is.EqualTo(0));
            Assert.That(game.View(2).OwnCards.Single(c=>c.CardId==Dodge).Zone,Is.EqualTo(CardZone.Discarded));Restore(catalog,game);
        }
        [Test]
        public void NoDiscardForSelectedHeroFinishesWithoutOfferingOtherZones()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugRecover,Dodge,target:2);
            int recoveredBefore=game.View(2).Events.Count(e=>e.Kind=="CardRecovered");
            Select(game);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,TargetChoice,"hero:2");
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution,Is.Null);
            Assert.That(game.View(2).OwnCards.Single(c=>c.CardId=="tigerclaw-02-偷袭").Zone,Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(2).Events.Count(e=>e.Kind=="CardRecovered"),Is.EqualTo(recoveredBefore));
        }
        [Test]
        public void FrozenRaidSaveStillOffersSecondMoveUnderItsRecordedEngine()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine14-raid-after.json"));
            var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));
            Assert.That(game.View(0).EngineVersion,Is.EqualTo(14));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Drum));
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-10));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UnitMoved"),Is.EqualTo(2));Restore(catalog,game);
        }
        [TestCase(3,0,0,true)] [TestCase(4,0,0,false)] [TestCase(4,1,0,true)] [TestCase(4,0,9,false)]
        public void TargetDistanceUsesRangedBonusAndNotSkillRangeBonus(int distance,int rangedBonus,int rangeBonus,bool eligible)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var origin=new Hex(7,-10);
            var destination=game.View(0).DebugTeleports["hero:2"].First(h=>h.Distance(origin)==distance && game.View(0).DebugTeleports["hero:3"].Any(n=>n.Distance(h)==1));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:destination);
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:game.View(0).DebugTeleports["hero:3"].First(h=>h.Distance(destination)==1));
            Select(game);var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());
            // Host-seeded item bonuses isolate numeric policy; replay coverage uses real-command cases above.
            state.Players[0].RangedBonus=rangedBonus;state.Players[0].RangeBonus=rangeBonus;game=new GameSession(catalog,codec,state);
            Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).EffectTargets.Contains("hero:2"),Is.EqualTo(eligible));
            Assert.That(game.View(2).EffectTargets,Is.Empty);
        }
        [TestCase("melee",Team.Red,true)] [TestCase("ranged",Team.Red,true)] [TestCase("heavy",Team.Red,true)] [TestCase("melee",Team.Blue,false)]
        public void AdjacentEnemyMinionsQualifyEvenIfProtectedButFriendlyMinionsDoNot(string kind,Team team,bool eligible)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var origin=new Hex(7,-10);
            foreach(int seat in new[]{1,3})Apply(game,0,CommandKind.DebugTeleport,"hero:"+seat,cell:game.View(0).DebugTeleports["hero:"+seat].First(h=>h.Distance(origin)>5));
            string unit=game.View(null).Units.First(u=>u.Kind==kind && u.Team==team).Id;
            Apply(game,0,CommandKind.DebugTeleport,unit,cell:game.View(0).DebugTeleports[unit].First(h=>h.Distance(origin)==1));
            Select(game);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectTargets.Contains("hero:0"),Is.EqualTo(eligible));
            Assert.That(game.View(0).Units.Any(u=>u.Id==unit),Is.True);
            if(eligible){Apply(game,0,TargetChoice,"hero:0");Apply(game,0,CommandKind.ChooseRecoveredCard,Axe);}
            Restore(catalog,game);
        }
        [Test]
        public void SilencePreventsStartingTheRecoverySkill()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Select(game,silence:true);
            Assert.That(game.View(0).CanBeginPrimary,Is.False);string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.BeginPrimary)).Code,Is.EqualTo("primary_restricted"));
            Assert.That(game.ExportSave(),Is.EqualTo(before));Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Axe).Zone,Is.EqualTo(CardZone.Discarded));
        }
    }
}

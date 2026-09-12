using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
using static Goa2.Tests.WarDrumTests;

namespace Goa2.Tests
{
    public sealed class MinstrelTests
    {
        internal const string Minstrel="brogan-10-吟游诗人",Sneak="tigerclaw-02-偷袭",Static="wasp-06-静电封锁";
        private static GameSession Restore(ContentCatalog catalog,GameSession game)
        {string saved=game.ExportSave();var restored=LocalGameFactory.Restore(catalog,saved);Assert.That(restored.ExportSave(),Is.EqualTo(saved));return restored;}
        [Test]
        public void ExactContractAndVersionGate()
        {
            var card=BattlefieldTests.Catalog().Card(Minstrel);
            Assert.That(card.Initiative,Is.EqualTo(4));Assert.That(card.PrimaryFamily,Is.EqualTo("skill"));Assert.That(card.PrimaryValue,Is.Zero);
            Assert.That(card.Subtype,Is.EqualTo("远程"));Assert.That(card.SubtypeValue,Is.EqualTo(3));
            Assert.That(card.SecondaryDefense,Is.EqualTo(5));Assert.That(card.SecondaryMovement,Is.EqualTo(2));
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,15),Is.False);
        }
        [TestCase(0,Axe,false)] [TestCase(2,Dodge,false)] [TestCase(2,Sneak,false)] [TestCase(2,Sneak,true)] [TestCase(0,Axe,true)]
        public void RecipientCanRetrieveExactlyOneAllowedCardOrSkip(int recipient,string recovered,bool skip)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,Minstrel);Select(game,Minstrel);
            Apply(game,0,CommandKind.BeginPrimary);game=Restore(catalog,game);Apply(game,0,TargetChoice,"hero:"+recipient);game=Restore(catalog,game);
            Assert.That(game.View(recipient).RecoverableCards,Is.EquivalentTo(recipient==0?new[]{Axe}:new[]{Dodge,Sneak}));
            Assert.That(game.View(null).RecoverableCards,Is.Empty);Assert.That(game.View(recipient==0?2:0).RecoverableCards,Is.Empty);
            var oldZone=game.View(recipient).OwnCards.Single(c=>c.CardId==recovered).Zone;
            var command=Cmd(game,recipient,CommandKind.ChooseRecoveredCard,skip?"skip":recovered);
            Assert.That(game.Execute(recipient,command).Accepted,Is.True);game=Restore(catalog,game);string after=game.ExportSave();
            Assert.That(game.Execute(recipient,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.View(recipient).OwnCards.Single(c=>c.CardId==recovered).Zone,Is.EqualTo(skip?oldZone:CardZone.InHand));
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Minstrel).Zone,Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(3));
        }
        [Test]
        public void SourceCurrentCardOtherPlayersAndInHandCardsAreRejectedWithoutMutation()
        {
            var game=Setup(BattlefieldTests.Catalog(),Minstrel);Select(game,Minstrel);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,TargetChoice,"hero:0");
            string before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,0,CommandKind.ChooseRecoveredCard,Minstrel),Cmd(game,0,CommandKind.ChooseRecoveredCard,"brogan-00-猛攻"),
                Cmd(game,0,CommandKind.ChooseRecoveredCard,Sneak),Cmd(game,2,CommandKind.ChooseRecoveredCard,Axe),Cmd(game,0,CommandKind.ChooseRecoveredCard,"brogan-12-一人成军")})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
        }
        [Test]
        public void AnAllyUnresolvedCardCannotBeRetrievedAndTheirActionStillHappens()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,Minstrel);
            string[] cards={Minstrel,"sabina-00-近身射击","tigerclaw-07-伺机待发","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Apply(game,1,CommandKind.Pass);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,TargetChoice,"hero:2");
            Assert.That(game.View(2).RecoverableCards,Is.EqualTo(new[]{Dodge}));string before=game.ExportSave();
            Assert.That(game.Execute(2,Cmd(game,2,CommandKind.ChooseRecoveredCard,cards[2])).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));
            Apply(game,2,CommandKind.ChooseRecoveredCard,Dodge);Apply(game,3,CommandKind.Pass);
            Assert.That(game.View(null).ActiveSeat,Is.EqualTo(2));Assert.That(game.View(2).OwnCards.Single(c=>c.CardId==cards[2]).Zone,Is.EqualTo(CardZone.PlayedUnresolved));
        }
        [Test]
        public void RecoveredResolvedAttackRetainsHistoryAndCanBePlayedNextTurn()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,Minstrel);Select(game,Minstrel);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,TargetChoice,"hero:2");Apply(game,2,CommandKind.ChooseRecoveredCard,Sneak);
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(CombatRules.CardTextModifier(catalog,state,catalog.Card("sabina-03-神枪手"),state.Units.Single(u=>u.Seat==1),state.Units.Single(u=>u.Seat==2)).Amount,Is.EqualTo(2));
            Assert.That(game.View(null).Players[2].Plays.Single().CardId,Is.EqualTo(Sneak));
            Apply(game,3,CommandKind.Pass);
            string[] cards={"brogan-13-冲拳","sabina-01-拔枪",Sneak,"arien-01-汹涌"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            if(game.View(0).Phase==Phase.InitiativeChoice)Apply(game,game.View(0).Pending!.ChooserSeat,CommandKind.ChooseInitiative,target:2);
            Assert.That(game.View(null).Players[2].Plays.Select(p=>p.Turn),Is.EqualTo(new[]{1,2}));
            Assert.That(game.View(null).Players[2].Plays.All(p=>p.CardId==Sneak),Is.True);
            Assert.That(game.View(2).OwnCards.Count(c=>c.CardId==Sneak),Is.EqualTo(1));Restore(catalog,game);
        }
        [Test]
        public void SourceMayRetrieveItsOwnPreviouslyResolvedCardWithoutChangingItems()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,Minstrel);Select(game,"brogan-13-冲拳");Apply(game,0,CommandKind.Pass);Apply(game,0,CommandKind.DebugAdvance,"turn");
            string[] cards={Minstrel,"sabina-01-拔枪","tigerclaw-07-伺机待发","arien-01-汹涌"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            while(game.View(0).ActiveSeat!=0)
            {
                var v=game.View(0);
                if(v.Phase==Phase.InitiativeChoice)Apply(game,v.Pending!.ChooserSeat,CommandKind.ChooseInitiative,target:v.Pending.CandidateSeats.First());
                else Apply(game,v.ActiveSeat!.Value,CommandKind.Pass);
            }
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,TargetChoice,"hero:0");
            var bonuses=game.View(0).Players[0].PermanentBonuses.ToArray();Apply(game,0,CommandKind.ChooseRecoveredCard,"brogan-13-冲拳");
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId=="brogan-13-冲拳").Zone,Is.EqualTo(CardZone.InHand));
            Assert.That(game.View(0).Players[0].PermanentBonuses.ToArray(),Is.EqualTo(bonuses));Restore(catalog,game);
        }
        [TestCase(false)] [TestCase(true)]
        public void RetrievingTheResolvedAuraCancelsItWhileSkippingPreservesIt(bool skip)
        {
            var catalog=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(catalog,"minstrel-aura",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,sabina,wasp,arien");Apply(game,0,CommandKind.DebugEquipCard,Minstrel,target:0);
            Apply(game,0,CommandKind.DebugEquipCard,Static,target:2);Apply(game,0,CommandKind.DebugSetCoin,"blue");
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(8,-10));
            string[] cards={Minstrel,"sabina-00-近身射击",Static,"arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Apply(game,1,CommandKind.Pass);Apply(game,2,CommandKind.BeginPrimary);
            Assert.That(game.View(null).Effects.Single().SourceCardId,Is.EqualTo(Static));
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,TargetChoice,"hero:2");game=Restore(catalog,game);
            Apply(game,2,CommandKind.ChooseRecoveredCard,skip?"skip":Static);
            Assert.That(game.View(null).Effects.Count,Is.EqualTo(skip?1:0));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="EffectCancelled" && e.CardId==Static),Is.EqualTo(skip?0:1));
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(3));Restore(catalog,game);
        }
        [Test]
        public void FrozenEngine15AllyChoiceStillRejectsResolvedCardsAndRestoresExactly()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine15-war-drum-recovery.json"));
            var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Minstrel));
            Assert.That(game.View(2).RecoverableCards,Is.EqualTo(new[]{Dodge}));
            Assert.That(game.Execute(2,Cmd(game,2,CommandKind.ChooseRecoveredCard,Sneak)).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(save));
            Apply(game,2,CommandKind.ChooseRecoveredCard,Dodge);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(3));Restore(catalog,game);
        }
        [TestCase(15,false)] [TestCase(16,true)]
        public void WarDrumRetrievingReflectionCancelsOnlyUnderNewRulesAndKeepsPrivateSourcePrivate(int engine,bool cancelled)
        {
            const string reflection="wasp-10-反射屏障";
            var catalog=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(catalog,"drum-reflection",new[]{"A","B","C","D"},42,true,engine);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,sabina,wasp,arien");Apply(game,0,CommandKind.DebugEquipCard,Drum,target:0);
            Apply(game,0,CommandKind.DebugEquipCard,reflection,target:2);Apply(game,0,CommandKind.DebugSetCoin,"blue");
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(6,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-9));Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(8,-10));
            string[] cards={Drum,"sabina-01-拔枪","wasp-00-闪耀之刃","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Apply(game,2,CommandKind.Pass);Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:2");
            Apply(game,2,CommandKind.Defend,reflection);Apply(game,1,CommandKind.ForcedDiscard,"sabina-00-近身射击");
            Assert.That(game.View(2).Effects.Single().SourceCardId,Is.EqualTo(reflection));
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,TargetChoice,"hero:2");game=Restore(catalog,game);
            Apply(game,2,CommandKind.ChooseRecoveredCard,reflection);
            Assert.That(game.View(2).Effects.Count,Is.EqualTo(cancelled?0:1));
            Assert.That(game.View(2).Events.Count(e=>e.Kind=="EffectCancelled" && e.CardId==reflection),Is.EqualTo(cancelled?1:0));
            Assert.That(game.View(null).Events.Any(e=>e.Kind=="EffectCancelled" && e.CardId==reflection),Is.False);
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="ProtectionExpired"),Is.EqualTo(cancelled?1:0));Restore(catalog,game);
        }
    }
}

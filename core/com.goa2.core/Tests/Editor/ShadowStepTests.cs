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
    public sealed class ShadowStepTests
    {
        internal const string Shadow="tigerclaw-16-暗影步",Cost="tigerclaw-00-瞬闪打击";
        private static CommandKind SwapKind => CommandKind.ChooseCardSwap;
        internal static GameSession Setup(ContentCatalog catalog,bool push=false,bool secondAttacker=false)
        {
            var game=LocalGameFactory.Create(catalog,"shadow-step",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"sabina,tigerclaw,brogan,arien");Apply(game,0,CommandKind.DebugEquipCard,Shadow,target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(push?7:8,-8));
            if(secondAttacker){Apply(game,0,CommandKind.DebugEquipCard,"brogan-02-投掷飞斧",target:2);Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(6,-9));}
            var cards=new[]{push?"sabina-00-近身射击":"sabina-01-拔枪","tigerclaw-07-伺机待发",secondAttacker?"brogan-02-投掷飞斧":"brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);return game;
        }
        private static void Attack(GameSession game){Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");}
        private static GameSession Restore(ContentCatalog catalog,GameSession game){var save=game.ExportSave();var result=LocalGameFactory.Restore(catalog,save);Assert.That(result.ExportSave(),Is.EqualTo(save));return result;}
        [Test]
        public void ExactContractAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Shadow);Assert.That(card.PrimaryFamily,Is.EqualTo("defense"));Assert.That(card.PrimaryValue,Is.Zero);Assert.That(card.Initiative,Is.EqualTo(11));Assert.That(card.SecondaryMovement,Is.EqualTo(3));
            Assert.That(CombatRules.HasDefenseProgram(card),Is.True);Assert.That(CombatRules.HasDefenseProgram(card,24),Is.False);card.Text+="然后再次攻击。";Assert.That(CombatRules.HasDefenseProgram(card),Is.False);
        }
        [TestCase(false,false)] [TestCase(false,true)] [TestCase(true,false)] [TestCase(true,true)]
        public void MovementAndSwapAreIndependentOptionalSteps(bool move,bool swap)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);Apply(game,1,CommandKind.Defend,Shadow);game=Restore(catalog,game);int hand=game.View(1).Players[1].HandCount;
            var destination=game.View(1).EffectMoves.First().Destination;if(move)Apply(game,1,CommandKind.ChooseEffectMove,cell:destination);else Apply(game,1,CommandKind.ChooseEffectMove,"skip");game=Restore(catalog,game);
            Assert.That(game.View(1).Pending!.Kind,Is.EqualTo("card_swap"));Assert.That(game.View(1).Pending!.ChooserSeat,Is.EqualTo(1));Assert.That(game.View(0).Pending!.Source,Is.Empty);
            Apply(game,1,SwapKind,swap?Cost:"skip");game=Restore(catalog,game);var own=game.View(1);
            Assert.That(own.OwnCards.Single(c=>c.CardId==Shadow).Zone,Is.EqualTo(swap?CardZone.InHand:CardZone.Discarded));Assert.That(own.OwnCards.Single(c=>c.CardId==Cost).Zone,Is.EqualTo(swap?CardZone.Discarded:CardZone.InHand));Assert.That(own.Players[1].HandCount,Is.EqualTo(hand));
            Assert.That(own.Units.Single(u=>u.Seat==1).Position,Is.EqualTo(move?destination:new Hex(8,-8)));Assert.That(own.Pending,Is.Null);Assert.That(own.ActiveSeat,Is.EqualTo(2));Assert.That(own.RedCrystal,Is.EqualTo(7));
            Assert.That(own.Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));Assert.That(own.Events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));Assert.That(own.Events.Count(e=>e.Kind=="CardsSwapped"),Is.EqualTo(swap?1:0));Assert.That(own.Events.Count(e=>e.Kind=="CardDiscarded"),Is.EqualTo(1));
            Assert.That(game.View(null).Events.Any(e=>e.CardId==Shadow || e.CardId==Cost),Is.False);
        }
        [Test]
        public void EmptyHandStillAllowsMovementButDoesNotOpenSwap()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);foreach(var card in game.View(1).OwnCards.Where(c=>c.Zone==CardZone.InHand && c.CardId!=Shadow).ToList())Apply(game,0,CommandKind.DebugDiscard,card.CardId,target:1);
            Attack(game);Apply(game,1,CommandKind.Defend,Shadow);Assert.That(game.View(1).EffectMoves,Is.Not.Empty);Apply(game,1,CommandKind.ChooseEffectMove,"skip");game=Restore(catalog,game);Assert.That(game.View(1).Pending,Is.Null);Assert.That(game.View(1).Players[1].HandCount,Is.Zero);Assert.That(game.View(1).Events.Any(e=>e.Kind=="CardSwapChoiceRequired"),Is.False);
        }
        [Test]
        public void NoMovementRouteStillOffersSwap()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());var source=state.Units.Single(u=>u.Seat==1);
            foreach(var cell in source.Position.Neighbors().Where(p=>catalog.Cell(p)?.Obstacle==false && !state.Units.Any(u=>u.Position==p)))state.Units.Add(new UnitState{Id="block:"+cell,Position=cell,Kind="melee",Team=Team.Red});
            var rules=new GameRules();rules.Apply(catalog,state,new Command{ActorSeat=1,Kind=CommandKind.Defend,Value=Shadow});Assert.That(state.Pending!.Kind,Is.EqualTo("card_swap"));rules.Apply(catalog,state,new Command{ActorSeat=1,Kind=SwapKind,Value=Cost});Assert.That(state.Players[1].Cards.Single(c=>c.CardId==Shadow).Zone,Is.EqualTo(CardZone.InHand));Assert.That(state.Events.Any(e=>e.Kind=="UnitMoved"),Is.False);
        }
        [Test]
        public void InvalidSwapAndDuplicateCommandsDoNotPartiallyExchangeCards()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);Apply(game,1,CommandKind.Defend,Shadow);Apply(game,1,CommandKind.ChooseEffectMove,"skip");string before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,0,SwapKind,Cost),Cmd(game,1,SwapKind,Shadow),Cmd(game,1,SwapKind,"tigerclaw-07-伺机待发"),Cmd(game,1,SwapKind,"sabina-00-近身射击"),Cmd(game,1,SwapKind,"unknown")})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var valid=Cmd(game,1,SwapKind,Cost);Assert.That(game.Execute(1,valid).Accepted,Is.True);string after=game.ExportSave();Assert.That(game.Execute(1,valid).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));Restore(catalog,game);
        }
        [Test]
        public void PointBlankPushCompletesBeforeMovementAndSwap()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,true);Attack(game);Apply(game,1,CommandKind.Defend,Shadow);Assert.That(game.View(1).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(8,-8)));Apply(game,1,CommandKind.ChooseEffectMove,cell:new Hex(8,-10));game=Restore(catalog,game);Apply(game,1,SwapKind,Cost);game=Restore(catalog,game);
            var events=game.View(1).Events;Assert.That(events.Single(e=>e.Kind=="UnitPushed").Sequence,Is.LessThan(events.Single(e=>e.Kind=="UnitMoved").Sequence));Assert.That(events.Single(e=>e.Kind=="UnitMoved").Sequence,Is.LessThan(events.Single(e=>e.Kind=="CardsSwapped").Sequence));Assert.That(events.Count(e=>e.Kind=="CardResolved"),Is.EqualTo(1));
        }
        [Test]
        public void SwappingKeepsHistoryFactsAndPermanentBonuses()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);Apply(game,1,CommandKind.Defend,Shadow);Apply(game,1,CommandKind.ChooseEffectMove,"skip");var state=new JsonStateCodec().Read(game.ExportSave());
            // Isolated state supplies old play history, as if an earlier effect had returned this card to hand.
            var cost=state.Players[1].Cards.Single(c=>c.CardId==Cost);cost.PlayedRound=1;cost.PlayedTurn=1;state.Players[1].RangedBonus=2;
            new GameRules().Apply(catalog,state,new Command{ActorSeat=1,Kind=SwapKind,Value=Cost});Assert.That(cost.PlayedRound,Is.EqualTo(1));Assert.That(cost.PlayedTurn,Is.EqualTo(1));Assert.That(state.Players[1].RangedBonus,Is.EqualTo(2));Assert.That(state.Events.Count(e=>e.Kind=="CardRevealed"),Is.EqualTo(4));
        }
        [Test]
        public void NonRangedAndUnblockableAttacksNeverStartTheFollowup()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());
            foreach(bool ranged in new[]{false,true}){state.Execution!.Attack!.Ranged=ranged;state.Execution.Attack.Unblockable=ranged;Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o=>o.CardId==Shadow),Is.False);}
        }
        [Test]
        public void OnlyTheOwnerReceivesLegalHandCardsAndPublicColorsUpdateAfterSwap()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);Apply(game,1,CommandKind.Defend,Shadow);Apply(game,1,CommandKind.ChooseEffectMove,"skip");var view=game.View(1);
            Assert.That(view.CardSwapOptions,Is.EquivalentTo(view.OwnCards.Where(c=>c.Zone==CardZone.InHand).Select(c=>c.CardId)));Assert.That(game.View(0).CardSwapOptions,Is.Empty);Assert.That(game.View(null).CardSwapOptions,Is.Empty);
            Assert.That(game.View(0).Pending!.Source,Is.Empty);Apply(game,1,SwapKind,Cost);game=Restore(catalog,game);Assert.That(game.View(0).Players[1].DiscardColors,Is.EqualTo(new[]{"gold"}));Assert.That(game.View(0).Events.Single(e=>e.Kind=="CardSwapColorsShown").Detail,Is.EqualTo("blue:gold"));
        }
        [Test]
        public void ReturnedShadowStepCanDefendAgainAgainstTheNextAttacker()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,secondAttacker:true);Attack(game);Apply(game,1,CommandKind.Defend,Shadow);Apply(game,1,CommandKind.ChooseEffectMove,cell:new Hex(8,-10));Apply(game,1,SwapKind,Cost);game=Restore(catalog,game);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));Apply(game,2,CommandKind.BeginPrimary);Apply(game,2,CommandKind.ChooseOptionalDiscard,"brogan-00-猛攻");Apply(game,2,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(1).DefenseOptions.Any(o=>o.CardId==Shadow),Is.True);Apply(game,1,CommandKind.Defend,Shadow);Apply(game,1,CommandKind.ChooseEffectMove,"skip");Apply(game,1,SwapKind,"skip");game=Restore(catalog,game);
            Assert.That(game.View(1).OwnCards.Single(c=>c.CardId==Shadow).Zone,Is.EqualTo(CardZone.Discarded));Assert.That(game.View(0).RedCrystal,Is.EqualTo(7));Assert.That(game.View(1).Events.Count(e=>e.Kind=="CardsSwapped"),Is.EqualTo(1));Assert.That(game.View(1).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(2));
        }
        [Test]
        public void FrozenSidestepStillFinishesWithoutSwap()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine24-sidestep-move.json"));var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Apply(game,1,CommandKind.ChooseEffectMove,"skip");game=Restore(catalog,game);Assert.That(game.View(1).Pending,Is.Null);Assert.That(game.View(1).SupportedDefenseCards,Does.Not.Contain(Shadow));
        }
    }
}

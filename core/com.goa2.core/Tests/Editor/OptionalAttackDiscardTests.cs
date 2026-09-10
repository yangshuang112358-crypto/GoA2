#nullable enable
using System;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed partial class OptionalAttackDiscardTests
    {
        internal const string Axe="brogan-02-投掷飞斧", Spear="brogan-04-投掷长矛", Cost="brogan-00-猛攻";
        internal static GameSession Ready(ContentCatalog catalog,string card,int distance=1,int engineVersion=GameState.CurrentEngineVersion,bool reflection=false)
        {
            var game=LocalGameFactory.Create(catalog,"optional-attack-discard",new[] {"A","B","C","D"},42,true,engineVersion);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,wasp,shargatha,arien");
            Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            if(reflection) Apply(game,0,CommandKind.DebugEquipCard,"wasp-10-反射屏障",target:1);
            var source=new Hex(6,-8);
            var target=catalog.Cells.Where(c=>!c.Obstacle && c.Region=="redFountain" && c.Position.Distance(source)==distance && !game.View(null).Units.Any(u=>u.Position==c.Position))
                .OrderBy(c=>c.Position.X).ThenBy(c=>c.Position.Y).First().Position;
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:source); Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:target);
            for(int seat=0;seat<4;seat++)
            {
                string choice=seat==0 ? card : game.View(seat).OwnCards.Select(c=>catalog.Card(c.CardId)).Where(c=>c.Color!="gold" && c.PrimaryFamily!="defense").OrderBy(c=>c.Initiative).ThenBy(c=>c.Id,StringComparer.Ordinal).First().Id;
                Apply(game,seat,CommandKind.SelectCard,choice);
            }
            // Wasp's lowest non-defense card has initiative 8, ahead of the axe's 7.
            if(game.View(null).ActiveSeat==1) Apply(game,1,CommandKind.Pass);
            Assert.That(game.View(null).ActiveSeat,Is.EqualTo(0)); return game;
        }
        internal static CommandKind DiscardCommand => (CommandKind)Enum.Parse(typeof(CommandKind),"ChooseOptionalDiscard");
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void AttackStartsWithAnExplicitOptionalOwnDiscardBeforeTargets(string card)
        {
            var game=Ready(BattlefieldTests.Catalog(),card);
            Apply(game,0,CommandKind.BeginPrimary);
            var view=game.View(0);
            Assert.That(view.Pending!.Kind,Is.EqualTo("optional_discard")); Assert.That(view.Pending.Optional,Is.True);
            Assert.That(view.Pending.ChooserSeat,Is.EqualTo(0)); Assert.That(view.Pending.Source,Is.EqualTo(card));
            Assert.That(view.CanPass,Is.False); Assert.That(view.SecondaryMoves,Is.Empty); Assert.That(view.AttackTargets,Is.Empty);
            Assert.That(view.Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);
        }
        [TestCase(Axe,3)]
        [TestCase(Spear,4)]
        public void PayingOneOwnCardMakesDistanceThreeLegalWithoutIncreasingAttack(string card,int attack)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,card,3);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,DiscardCommand,Cost);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Cost).Zone,Is.EqualTo(CardZone.Discarded));
            Assert.That(game.View(0).AttackTargets,Does.Contain("hero:1"));
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(null).Attack!.Ranged,Is.True); Assert.That(game.View(null).Attack!.BaseAttack,Is.EqualTo(attack));
            Assert.That(game.View(null).Attack!.CardTextBonus,Is.Zero);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Axe)]
        [TestCase(Spear)]
        public void ExplicitSkipKeepsTheHandAndPermitsTheAdjacentRangedAttack(string card)
        {
            var game=Ready(BattlefieldTests.Catalog(),card);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,DiscardCommand,"skip");
            Assert.That(game.View(0).OwnCards.Count(c=>c.Zone==CardZone.InHand),Is.EqualTo(4));
            Assert.That(game.View(null).Players[0].DiscardColors,Is.Empty);
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1"); Assert.That(game.View(null).Attack!.Ranged,Is.True);
        }
        [TestCase(Axe,false)]
        [TestCase(Spear,true)]
        public void ExistingDiscardOnlyExtendsTheSpearWhenTheNewOptionalCostIsSkipped(string card,bool extended)
        {
            var game=Ready(BattlefieldTests.Catalog(),card,3); Apply(game,0,CommandKind.DebugDiscard,Cost,target:0);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,DiscardCommand,"skip");
            Assert.That(game.View(0).AttackTargets.Contains("hero:1"),Is.EqualTo(extended));
            Assert.That(game.View(null).Players[0].DiscardColors.Count,Is.EqualTo(1));
            Assert.That(game.View(0).OwnCards.Count(c=>c.Zone==CardZone.InHand),Is.EqualTo(3));
        }
    }
}

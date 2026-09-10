using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class CombatQueryTests
    {
        private static GameState Arena(ContentCatalog catalog, string defenderHero = "wasp")
        {
            var state = new GameRules().Create(catalog, "query", new[] { "A", "B", "C", "D" }, 42);
            state.Players[0].HeroId = "sabina"; state.Players[1].HeroId = defenderHero;
            foreach (var player in state.Players.Take(2))
                player.Cards = catalog.Cards.Where(c => c.HeroId == player.HeroId && (c.Color == "gold" || c.Color == "silver" || c.Level == 1))
                    .Select(c => new CardInstance { CardId = c.Id, Zone = CardZone.InHand }).ToList();
            state.Players[0].Cards.Single(c => c.CardId == "sabina-01-拔枪").Zone = CardZone.PlayedUnresolved;
            state.ActiveSeat = 0; state.Phase = Phase.Action;
            state.Units = new List<UnitState>
            {
                new UnitState { Id="hero:0",Seat=0,Team=Team.Blue,Kind="hero",Position=new Hex(0,0) },
                new UnitState { Id="hero:1",Seat=1,Team=Team.Red,Kind="hero",Position=new Hex(2,0) }
            };
            return state;
        }
        private static void Response(GameState state, bool ranged=true, bool unblockable=false)
        {
            state.Phase = Phase.EffectChoice; state.Pending = new PendingChoice { Kind="defense",ChooserSeat=1,Source="sabina-01-拔枪" };
            state.Execution = new CardExecution { ControllerSeat=0,CardId="sabina-01-拔枪",Attack=new AttackBreakdown
                { AttackerSeat=0,DefenderSeat=1,TargetUnitId="hero:1",BaseAttack=4,FinalAttack=4,Ranged=ranged,Unblockable=unblockable } };
        }
        [Test]
        public void OnlyReviewedAttackTextIsBoundToTheProgram()
        {
            var catalog = BattlefieldTests.Catalog();
            Assert.That(CombatRules.HasPrimaryProgram(catalog.Card("sabina-01-拔枪")), Is.True);
            Assert.That(CombatRules.HasPrimaryProgram(catalog.Card("shargatha-02-快速突刺")), Is.True);
            Assert.That(CombatRules.HasPrimaryProgram(catalog.Card("sabina-00-近身射击")), Is.False);
            catalog.Card("sabina-01-拔枪").Text += "攻击后：移动1格。";
            Assert.That(CombatRules.HasPrimaryProgram(catalog.Card("sabina-01-拔枪")), Is.False);
        }
        [Test]
        public void TargetsRespectMinimumDistanceRangedBonusTeamAndHeavyProtection()
        {
            var catalog = BattlefieldTests.Catalog(); var state = Arena(catalog);
            state.Units.Add(new UnitState { Id="enemy-near",Kind="melee",Team=Team.Red,Position=new Hex(1,0) });
            state.Units.Add(new UnitState { Id="enemy-far",Kind="melee",Team=Team.Red,Position=new Hex(3,0) });
            state.Units.Add(new UnitState { Id="heavy",Kind="heavy",Team=Team.Red,Position=new Hex(0,2) });
            state.Units.Add(new UnitState { Id="friend",Kind="melee",Team=Team.Blue,Position=new Hex(-2,0) });
            state.Players[0].RangeBonus = 10;
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Is.EqualTo(new[] { "hero:1" }));
            Assert.That(CombatRules.AttackTargets(catalog,state,1), Is.Empty);
            state.Players[0].RangedBonus = 1;
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Is.EquivalentTo(new[] { "hero:1","enemy-far" }));
            state.Units.RemoveAll(u => u.Id == "enemy-near" || u.Id == "enemy-far");
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Is.EquivalentTo(new[] { "hero:1","heavy" }));
            state.Pending = new PendingChoice { Kind="unrelated",ChooserSeat=0 };
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Is.Empty);
        }
        [Test]
        public void WaspBlockRequiresNonAdjacentRangedWhileTigerBlockAllowsAdjacentRanged()
        {
            var catalog = BattlefieldTests.Catalog(); var state = Arena(catalog); Response(state);
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId == "wasp-07-抵挡屏障" && o.Block), Is.True);
            state.Units[0].Position = new Hex(1,0);
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId == "wasp-07-抵挡屏障"), Is.False);
            state = Arena(catalog,"tigerclaw"); Response(state); state.Units[0].Position = new Hex(1,0);
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId == "tigerclaw-18-躲闪" && o.Assessment.Successful), Is.True);
            state.Execution!.Attack!.Ranged = false;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId == "tigerclaw-18-躲闪"), Is.False);
            state.Execution.Attack.Ranged = true; state.Execution.Attack.Unblockable = true;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.Block), Is.False);
            Assert.That(CombatRules.DefenseOptions(catalog,state,0), Is.Empty);
        }
        [Test]
        public void ChallengerIgnoresBothMinionSignsAndOnlyHandCardsCanRespond()
        {
            var catalog = BattlefieldTests.Catalog(); var state = Arena(catalog,"arien"); Response(state,unblockable:true);
            var attack = state.Execution!.Attack!;
            attack.BaseAttack=5; attack.EnemySupport=3; attack.FriendlyGuard=1; attack.FinalAttack=7;
            var option = CombatRules.DefenseOptions(catalog,state,1).Single(o => o.CardId == "arien-13-挑战者");
            Assert.That(option.Primary && option.IgnoresMinions && !option.Block && option.Assessment.Successful, Is.True);
            Assert.That(option.Assessment.AttackCompared, Is.EqualTo(5));
            foreach (var card in state.Players[1].Cards) card.Zone=CardZone.PlayedResolved;
            Assert.That(CombatRules.DefenseOptions(catalog,state,1), Is.Empty);
        }
        [Test]
        public void UnknownPrimaryDefenseStaysExplicitAndSecondaryDefenseDoesNotBorrowItsMainText()
        {
            var catalog = BattlefieldTests.Catalog(); var state = Arena(catalog); Response(state);
            state.EngineVersion=3;
            state.Players[1].Cards.Single(c => c.CardId == "wasp-07-抵挡屏障").CardId = "wasp-08-偏转屏障";
            Assert.That(CombatRules.UnimplementedDefenses(catalog,state,1), Is.EqualTo(new[] { "wasp-08-偏转屏障" }));
            Assert.That(CombatRules.UnimplementedDefenses(catalog,state,0), Is.Empty);
            var secondary = CombatRules.DefenseOptions(catalog,state,1).Single(o => o.CardId == "wasp-00-闪耀之刃");
            Assert.That(secondary.Primary || secondary.Block || secondary.IgnoresMinions, Is.False);
            Assert.That(secondary.Assessment.FinalDefense, Is.EqualTo(2));
            Assert.That(CombatRules.DefenseOptions(catalog,state,1).Any(o => o.CardId == "wasp-08-偏转屏障"), Is.False);
        }
    }
}

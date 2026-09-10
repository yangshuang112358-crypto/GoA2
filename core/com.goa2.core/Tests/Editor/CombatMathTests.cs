using Goa2.Domain;
using Goa2.Rules;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class CombatMathTests
    {
        private static GameState Arena()
        {
            var state = new GameRules().Create(FoundationTests.Fixture(), "math", new[] { "A", "B", "C", "D" }, 42);
            state.Units.Add(new UnitState { Id = "hero:0", Kind = "hero", Seat = 0, Team = Team.Blue, Position = new Hex(-2,0) });
            state.Units.Add(new UnitState { Id = "hero:1", Kind = "hero", Seat = 1, Team = Team.Red, Position = new Hex(0,0) });
            return state;
        }
        private static void Add(GameState state, string id, string kind, Team team, int x, int y) => state.Units.Add(new UnitState { Id = id, Kind = kind, Team = team, Position = new Hex(x,y) });
        [Test]
        public void AttackReportsEnemySupportAndOnlyFriendlyMeleeGuard()
        {
            var state = Arena(); state.Players[0].AttackBonus = 2;
            Add(state,"enemy-melee","melee",Team.Blue,-1,0);
            Add(state,"enemy-heavy","heavy",Team.Blue,1,0);
            Add(state,"enemy-ranged-two","ranged",Team.Blue,0,2);
            Add(state,"enemy-ranged-three","ranged",Team.Blue,0,3);
            Add(state,"friendly-melee","melee",Team.Red,0,1);
            Add(state,"friendly-heavy","heavy",Team.Red,1,-1);
            Add(state,"friendly-ranged","ranged",Team.Red,-1,2);
            var attack = CombatMath.Attack(state, new CardDefinition { Id = "fixture", PrimaryValue = 3, Subtype = "远程" }, 0, "hero:1");
            Assert.That(attack.BaseAttack, Is.EqualTo(3)); Assert.That(attack.AttackBonus, Is.EqualTo(2));
            Assert.That(attack.EnemySupport, Is.EqualTo(3)); Assert.That(attack.FriendlyGuard, Is.EqualTo(1));
            Assert.That(attack.FinalAttack, Is.EqualTo(7)); Assert.That(attack.Ranged, Is.True);
            Assert.That(attack.EnemySupportSources, Is.EquivalentTo(new[] { "enemy-melee", "enemy-heavy", "enemy-ranged-two" }));
            Assert.That(attack.FriendlyGuardSources, Is.EqualTo(new[] { "friendly-melee" }));
            Assert.That(state.Players[0].Gold, Is.Zero); Assert.That(state.Units.Count, Is.EqualTo(9));
        }
        [Test]
        public void NegativeAttackIsPreservedAndSkillRangeIsNotRanged()
        {
            var state = Arena();
            Add(state,"guard1","melee",Team.Red,1,0); Add(state,"guard2","melee",Team.Red,0,1); Add(state,"guard3","melee",Team.Red,1,-1);
            var attack = CombatMath.Attack(state, new CardDefinition { PrimaryValue = 1, Subtype = "范围", SubtypeValue = 5 }, 0, "hero:1");
            Assert.That(attack.FinalAttack, Is.EqualTo(-2)); Assert.That(attack.Ranged, Is.False);
            Assert.That(CombatMath.Defense(attack, 0, 0).Successful, Is.True);
        }
        [Test]
        public void DefenseUsesInclusiveComparisonAndIgnoringMinionsAffectsBothSigns()
        {
            var attack = new AttackBreakdown { BaseAttack = 5, AttackBonus = 1, EnemySupport = 3, FriendlyGuard = 2, FinalAttack = 7 };
            Assert.That(CombatMath.Defense(attack, 5, 1).Successful, Is.False);
            var defense = CombatMath.Defense(attack, 5, 1, ignoreMinions: true);
            Assert.That(defense.Successful, Is.True); Assert.That(defense.AttackCompared, Is.EqualTo(6));
            attack.EnemySupport = 0; attack.FriendlyGuard = 3; attack.FinalAttack = 3;
            Assert.That(CombatMath.Defense(attack, 5, 0).Successful, Is.True);
            Assert.That(CombatMath.Defense(attack, 5, 0, ignoreMinions: true).Successful, Is.False);
        }
        [Test]
        public void UnblockablePreventsBlockingButStillAllowsNumericDefense()
        {
            var attack = new AttackBreakdown { BaseAttack = 5, FinalAttack = 5, Unblockable = true };
            Assert.That(CombatMath.Defense(attack, 0, 0, block: true).Successful, Is.False);
            Assert.That(CombatMath.Defense(attack, 5, 0).Successful, Is.True);
            attack.Unblockable = false;
            Assert.That(CombatMath.Defense(attack, 0, 0, block: true).Successful, Is.True);
        }
    }
}

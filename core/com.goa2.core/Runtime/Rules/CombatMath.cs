#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public static class CombatMath
    {
        public static AttackBreakdown Attack(GameState state, CardDefinition card, int attackerSeat, string targetUnitId, int extraAttack = 0, bool unblockable = false,IReadOnlyDictionary<string,string>? minionKinds=null)
        {
            GameRules.Require(attackerSeat >= 0 && attackerSeat < state.Players.Count, "invalid_attacker", "攻击者不存在。");
            var target = state.Units.FirstOrDefault(u => u.Id == targetUnitId && u.Kind == "hero" && u.Seat.HasValue);
            GameRules.Require(target != null, "invalid_defender", "英雄防御目标不存在。");
            string Kind(UnitState unit) => minionKinds!=null && minionKinds.TryGetValue(unit.Id,out var kind) ? kind : unit.Kind;
            int Support(UnitState unit)
            {
                string kind=Kind(unit);int distance=unit.Position.Distance(target!.Position);
                return ((kind=="melee" || kind=="heavy" || kind=="melee_ranged") && distance==1 ? 1 : 0) +
                    ((kind=="ranged" || kind=="melee_ranged") && distance<=2 ? 1 : 0);
            }
            var result = new AttackBreakdown
            {
                SourceCardId = card.Id, TargetUnitId = targetUnitId, AttackerSeat = attackerSeat, DefenderSeat = target!.Seat!.Value,
                BaseAttack = card.PrimaryValue, AttackBonus = state.Players[attackerSeat].AttackBonus + extraAttack,
                CardTextBonus = extraAttack,
                Ranged = card.Subtype == "远程", Unblockable = unblockable,
                EnemySupportSources = state.Units.Where(u => u.Team != target.Team)
                    .SelectMany(u=>Enumerable.Repeat(u.Id,Support(u))).OrderBy(id => id, StringComparer.Ordinal).ToList(),
                FriendlyGuardSources = state.Units.Where(u => u.Team == target.Team && (Kind(u) == "melee" || Kind(u)=="melee_ranged") && u.Position.Distance(target.Position) == 1)
                    .Select(u => u.Id).OrderBy(id => id, StringComparer.Ordinal).ToList()
            };
            result.EnemySupport = result.EnemySupportSources.Count; result.FriendlyGuard = result.FriendlyGuardSources.Count;
            result.FinalAttack = result.BaseAttack + result.AttackBonus + result.EnemySupport - result.FriendlyGuard;
            return result;
        }
        public static DefenseAssessment Defense(AttackBreakdown attack, int baseDefense, int defenseBonus, bool ignoreMinions = false, bool block = false)
        {
            var result = new DefenseAssessment
            {
                BaseDefense = baseDefense, DefenseBonus = defenseBonus, FinalDefense = baseDefense + defenseBonus,
                AttackCompared = ignoreMinions ? attack.BaseAttack + attack.AttackBonus : attack.FinalAttack,
                IgnoresMinions = ignoreMinions, Blocked = block && !attack.Unblockable
            };
            result.Successful = block ? result.Blocked : result.FinalDefense >= result.AttackCompared;
            return result;
        }
    }
}

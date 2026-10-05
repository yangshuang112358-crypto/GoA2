using System;
using System.Collections.Generic;
using System.Linq;
namespace Goa2.Domain
{
    public static class MinionCombatBaseline
    {
        public static int SupportPoints(string kind,int distance) =>
            ((kind=="melee" || kind=="heavy" || kind=="melee_ranged") && distance==1 ? 1 : 0) +
            ((kind=="ranged" || kind=="melee_ranged") && distance<=2 ? 1 : 0);
        // Read-only baseline for presentation. Intentionally excludes card overrides,
        // and is never used to replace the actual attack/defense assessment.
        public static AttackBreakdown Sources(IEnumerable<UnitState> units,UnitState target)
        {
            var all=units.ToList();
            return new AttackBreakdown {
                TargetUnitId=target.Id,
                EnemySupportSources=all.Where(u=>u.Team!=target.Team && SupportPoints(u.Kind,u.Position.Distance(target.Position))>0).Select(u=>u.Id).OrderBy(x=>x,StringComparer.Ordinal).ToList(),
                FriendlyGuardSources=all.Where(u=>u.Team==target.Team && u.Kind=="melee" && u.Position.Distance(target.Position)==1).Select(u=>u.Id).OrderBy(x=>x,StringComparer.Ordinal).ToList()
            };
        }
    }
}

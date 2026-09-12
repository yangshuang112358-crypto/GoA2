#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public static class MovementRules
    {
        public static List<MoveOption> LegalMoves(ContentCatalog catalog, GameState state, int seat, MoveMode mode)
        {
            var empty = new List<MoveOption>();
            if (state.Phase != Phase.Action || state.Pending != null || state.Execution != null || state.ActiveSeat != seat || seat < 0 || seat > 3) return empty;
            var player = state.Players[seat];
            var played = player.Cards.SingleOrDefault(c => c.Zone == CardZone.PlayedUnresolved);
            var unit = state.Units.SingleOrDefault(u => u.Seat == seat);
            if (played == null || unit == null) return empty;
            var card = catalog.Card(played.CardId);
            bool primaryMove = card.PrimaryFamily == "movement" && card.PrimaryValue > 0;
            bool secondaryMove = card.PrimaryFamily != "movement" && card.SecondaryMovement > 0;
            if (mode == MoveMode.Secondary && secondaryMove)
                return Reachable(catalog, state, unit, card.SecondaryMovement!.Value + player.MovementBonus);
            if (mode == MoveMode.Fast && (primaryMove || secondaryMove))
                return Fast(catalog, state, unit);
            return empty;
        }
        internal static List<MoveOption> Reachable(ContentCatalog catalog, GameState state, UnitState unit, int budget)
        {
            var result = new List<MoveOption>();
            if (budget <= 0) return result;
            var origin = unit.Position;
            var cells = catalog.Cells.ToDictionary(c => c.Position);
            var occupied = new HashSet<Hex>(state.Units.Select(u => u.Position));
            var paths = new Dictionary<Hex, List<Hex>> { [origin] = new List<Hex> { origin } };
            var queue = new Queue<Hex>(); queue.Enqueue(origin);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (paths[current].Count - 1 >= budget) continue;
                foreach (var next in current.Neighbors())
                {
                    if (paths.ContainsKey(next) || occupied.Contains(next) || !cells.TryGetValue(next, out var cell) || cell.Obstacle) continue;
                    if (!EffectRules.CanMoveAcross(catalog,state,unit,current,next)) continue;
                    var path = new List<Hex>(paths[current]) { next };
                    paths.Add(next, path); queue.Enqueue(next);
                    result.Add(new MoveOption { Destination = next, Path = path });
                }
            }
            return result.OrderBy(o => o.Destination.X).ThenBy(o => o.Destination.Y).ToList();
        }
        private static List<MoveOption> Fast(ContentCatalog catalog, GameState state, UnitState source)
        {
            var cells = catalog.Cells.ToDictionary(c => c.Position);
            var origin = cells[source.Position];
            var enemyRegions = new HashSet<string>(state.Units.Where(u => u.Team != source.Team).Select(u => cells[u.Position].Region));
            if (enemyRegions.Contains(origin.Region)) return new List<MoveOption>();
            var allowedRegions = new HashSet<string> { origin.Region };
            foreach (var cell in catalog.Cells.Where(c => !c.Obstacle && c.Region == origin.Region))
                foreach (var neighbor in cell.Position.Neighbors())
                    if (cells.TryGetValue(neighbor, out var adjacent) && !adjacent.Obstacle) allowedRegions.Add(adjacent.Region);
            allowedRegions.ExceptWith(enemyRegions);
            var occupied = new HashSet<Hex>(state.Units.Select(u => u.Position));
            return catalog.Cells.Where(c => !c.Obstacle && allowedRegions.Contains(c.Region) && !occupied.Contains(c.Position) && EffectRules.CanMoveAcross(catalog,state,source,source.Position,c.Position))
                .OrderBy(c => c.Position.X).ThenBy(c => c.Position.Y)
                .Select(c => new MoveOption { Destination = c.Position, Path = new List<Hex> { source.Position, c.Position } }).ToList();
        }
    }
}

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        private static void DefeatHero(GameState state, Command command, string targetId, int killerSeat, string source)
        {
            var unit = state.Units.SingleOrDefault(u => u.Id == targetId && u.Kind == "hero" && u.Seat.HasValue);
            Require(unit != null && killerSeat >= 0 && killerSeat < 4 && state.Players[killerSeat].Team != unit.Team, "invalid_defeat", "请选择存在的敌方英雄。");
            var victim = state.Players[unit!.Seat!.Value];
            Require(victim.Level >= 1 && victim.Level <= 8, "invalid_level", "英雄等级无效。");
            state.Units.Remove(unit); victim.AwaitingRespawn = true;
            Emit(state, command, "HeroDefeated", victim.Seat, source == "debug" ? null : source, detail: "by:" + killerSeat);
            state.Events.Last().From = unit.Position;
            state.Players[killerSeat].Gold += victim.Level;
            Emit(state, command, "GoldAwarded", killerSeat, detail: victim.Level.ToString());
            int assist = new[] { 1, 1, 1, 2, 2, 2, 3, 3 }[victim.Level - 1];
            foreach (var ally in state.Players.Where(p => p.Team == state.Players[killerSeat].Team && p.Seat != killerSeat))
            {
                ally.Gold += assist; Emit(state, command, "AssistGoldAwarded", ally.Seat, detail: assist.ToString());
            }
            if (victim.Team == Team.Blue) state.BlueCrystal -= victim.Level; else state.RedCrystal -= victim.Level;
            Emit(state, command, "CrystalDamaged", victim.Seat, detail: victim.Level.ToString());
            if ((victim.Team == Team.Blue ? state.BlueCrystal : state.RedCrystal) <= 0) DeclareVictory(state, command, OtherTeam(victim.Team), "crystal");
        }
        public static List<Hex> LegalRespawns(ContentCatalog catalog, GameState state, int seat)
        {
            if (seat < 0 || seat > 3 || state.Phase != Phase.EffectChoice || state.Pending?.Kind != "hero_respawn" || state.Pending.ChooserSeat != seat ||
                state.ActiveSeat != seat || !state.Players[seat].AwaitingRespawn) return new List<Hex>();
            string spawn = state.Players[seat].Team == Team.Blue ? "blueHeroSpawn" : "redHeroSpawn";
            return catalog.Cells.Where(c => c.Spawn == spawn && !c.Obstacle && !state.Units.Any(u => u.Position == c.Position))
                .Select(c => c.Position).OrderBy(h => h.X).ThenBy(h => h.Y).ToList();
        }
        private static void RespawnHero(ContentCatalog catalog, GameState state, Command command)
        {
            Require(LegalRespawns(catalog, state, command.ActorSeat).Contains(command.Destination), "invalid_respawn", "请由待复活英雄本人选择本队空闲出生点。");
            var player = state.Players[command.ActorSeat];
            state.Units.Add(new UnitState { Id = "hero:" + player.Seat, Kind = "hero", Seat = player.Seat, Team = player.Team, Position = command.Destination });
            player.AwaitingRespawn = false;
            Emit(state, command, "HeroRespawned", player.Seat); state.Events.Last().To = command.Destination;
            BeginAction(catalog, state, command, player.Seat);
        }
    }
}

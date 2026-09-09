#nullable enable
using System;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        public GameState Create(ContentCatalog catalog, string matchId, string[] names, int seed)
        {
            Require(names.Length == 4 && names.All(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 40), "invalid_players", "需要四个有效玩家名称。");
            Require(!string.IsNullOrWhiteSpace(matchId) && matchId.Length <= 100, "invalid_match", "对局编号无效。");
            return new GameState
            {
                MatchId = matchId, ContentVersion = catalog.Version, ContentHash = catalog.Hash,
                RulesVersion = catalog.Rules.Version, Seed = seed, DecisionCoin = (seed & 1) == 0 ? Team.Blue : Team.Red,
                BlueCrystal = catalog.Rules.StartingCrystalLife, RedCrystal = catalog.Rules.StartingCrystalLife,
                VictoryMarksRequired = catalog.Rules.FrontlineVictoryMarks, CombatRegion = catalog.Rules.InitialCombatRegion,
                Players = names.Select((name, seat) => new PlayerState { Seat = seat, Team = seat % 2 == 0 ? Team.Blue : Team.Red, Name = name }).ToList()
            };
        }
        public void Apply(ContentCatalog catalog, GameState state, Command command)
        {
            Require(command.ActorSeat >= 0 && command.ActorSeat < 4, "invalid_actor", "无效席位。");
            switch (command.Kind)
            {
                case CommandKind.ChooseHero: ChooseHero(catalog, state, command); break;
                case CommandKind.DeployHero: DeployHero(catalog, state, command); break;
                case CommandKind.SelectCard: SelectCard(state, command); break;
                case CommandKind.ConfirmCard: ConfirmCard(catalog, state, command); break;
                case CommandKind.ChooseInitiative: ChooseInitiative(state, command); break;
                case CommandKind.Pass:
                    Require(state.Phase == Phase.Action && state.ActiveSeat == command.ActorSeat, "not_active", "当前不由你行动。");
                    Emit(state, command, "ActionPassed", command.ActorSeat, ActiveCard(state).CardId);
                    FinishAction(catalog, state, command);
                    break;
                default: throw new RuleViolation("unsupported_command", "此操作尚未实装。");
            }
        }
        private static void ChooseHero(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.HeroSelection, "wrong_phase", "当前不是英雄选择阶段。");
            Require(catalog.Heroes.Any(h => h.Id == command.Value), "unknown_hero", "英雄不存在。");
            Require(!state.Players.Any(p => p.Seat != command.ActorSeat && p.HeroId == command.Value), "hero_taken", "英雄已被其他席位选择。");
            var player = state.Players[command.ActorSeat];
            player.HeroId = command.Value;
            player.Cards = catalog.Cards.Where(c => c.HeroId == command.Value && (c.Color == "gold" || c.Color == "silver" || c.Level == 1))
                .Select(c => new CardInstance { CardId = c.Id, Zone = CardZone.InHand }).ToList();
            Require(player.Cards.Count == catalog.Rules.HandSize, "invalid_content", "英雄起始牌不完整。");
            Emit(state, command, "HeroChosen", command.ActorSeat, detail: command.Value);
            if (state.Players.All(p => p.HeroId != null))
            {
                state.Phase = Phase.Deployment;
                foreach (var cell in catalog.Cells.Where(c => c.Region == state.CombatRegion && c.Spawn.EndsWith("Spawn", StringComparison.Ordinal) && !c.Spawn.Contains("Hero")))
                {
                    bool blue = cell.Spawn.StartsWith("blue", StringComparison.Ordinal);
                    string kind = cell.Spawn.Contains("Heavy") ? "heavy" : cell.Spawn.Contains("Ranged") ? "ranged" : "melee";
                    state.Units.Add(new UnitState { Id = "minion:" + cell.Position, Kind = kind, Team = blue ? Team.Blue : Team.Red, Position = cell.Position });
                }
                Emit(state, command, "DeploymentStarted");
            }
        }
        internal static void Require(bool condition, string code, string message)
        {
            if (!condition) throw new RuleViolation(code, message);
        }
        internal static void Emit(GameState state, Command command, string kind, int? seat = null, string? cardId = null, int? privateTo = null, string detail = "")
        {
            state.Events.Add(new GameEvent { Sequence = state.Events.Count + 1L, Revision = state.Revision + 1, CommandId = command.Id, Kind = kind, Seat = seat, CardId = cardId, PrivateTo = privateTo, Detail = detail });
        }
    }
}

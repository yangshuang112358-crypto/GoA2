#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        public static bool CanResolveRoundEnd(GameState state) => state.Phase == Phase.RoundEnd && state.RoundEnd == null && state.Pending == null && state.Frontline == null && state.Execution == null;
        public static List<string> LegalRoundMinionRemovals(GameState state, int seat)
        {
            if (state.Phase != Phase.EffectChoice || state.Pending?.Kind != "round_minion_removal" || state.Pending.ChooserSeat != seat || state.RoundEnd?.Stage != "minion_battle" || state.Frontline != null)
                return new List<string>();
            var legal = LegalMinionRemovals(state);
            return state.Units.Where(u => u.Team == state.RoundEnd.LosingTeam && legal.Contains(u.Id)).Select(u => u.Id).OrderBy(id => id, System.StringComparer.Ordinal).ToList();
        }
        private static void ResolveRoundEnd(ContentCatalog catalog, GameState state, Command command)
        {
            Require(CanResolveRoundEnd(state), "invalid_round_end", "当前不能开始轮末结算。");
            Require(state.Turn == catalog.Rules.TurnsPerRound && state.Players.All(p => p.HeroId != null) && !state.Players.SelectMany(p => p.Cards).Any(c => c.Zone == CardZone.PlayedUnresolved), "invalid_round_end", "须完成第四回合的全部行动。");
            Emit(state, command, "RoundEndStarted", detail: state.Round.ToString());
            foreach (var player in state.Players)
            {
                int returned = player.Cards.Count(c => c.Zone != CardZone.InHand);
                foreach (var card in player.Cards) { card.Zone = CardZone.InHand; card.PlayedRound = null; card.PlayedTurn = null; }
                player.Confirmed = false;
                Emit(state, command, "CardsRecalled", player.Seat, detail: returned.ToString());
            }
            var progress = new RoundEndProgress
            {
                Round = state.Round,
                BlueMinions = state.Units.Count(u => IsMinion(u) && u.Team == Team.Blue),
                RedMinions = state.Units.Count(u => IsMinion(u) && u.Team == Team.Red)
            };
            progress.RemainingRemovals = System.Math.Abs(progress.BlueMinions - progress.RedMinions);
            if (progress.RemainingRemovals > 0) progress.LosingTeam = progress.BlueMinions < progress.RedMinions ? Team.Blue : Team.Red;
            state.RoundEnd = progress;
            Emit(state, command, "MinionBattleCounted", detail: progress.BlueMinions + ":" + progress.RedMinions + ":" + progress.RemainingRemovals);
            ContinueRoundMinionBattle(catalog, state, command);
        }
        private static void ContinueRoundMinionBattle(ContentCatalog catalog, GameState state, Command command)
        {
            if (state.Phase == Phase.Finished || state.RoundEnd == null || state.RoundEnd.Stage != "minion_battle") return;
            var progress = state.RoundEnd;
            if (progress.RemainingRemovals == 0 || !state.Units.Any(u => IsMinion(u) && u.Team == progress.LosingTeam))
            {
                progress.RemainingRemovals = 0; state.Pending = null; state.ActiveSeat = null; state.Phase = Phase.RoundEnd;
                Emit(state, command, "MinionBattleCompleted", detail: progress.Round.ToString());
                BeginUpgrades(catalog, state, command);
                return;
            }
            state.Phase = Phase.EffectChoice;
            state.Pending = new PendingChoice
            {
                Id = "round-minion:" + (state.Events.Count + 1), Kind = "round_minion_removal", ChooserSeat = Captain(state, progress.LosingTeam!.Value),
                Source = "R-ROUND-END", ResumeAt = "round_minion_battle", Optional = false
            };
            state.Pending.CandidateUnits = LegalRoundMinionRemovals(state, state.Pending.ChooserSeat);
            Emit(state, command, "RoundMinionChoiceRequired", state.Pending.ChooserSeat, detail: progress.RemainingRemovals.ToString());
        }
        private static void ChooseRoundMinionRemoval(ContentCatalog catalog, GameState state, Command command)
        {
            Require(LegalRoundMinionRemovals(state, command.ActorSeat).Contains(command.Value), "invalid_round_minion", "须由少兵方队长选择可移除的本队小兵。");
            bool heavy = state.Units.Single(u => u.Id == command.Value).Kind == "heavy";
            state.RoundEnd!.RemainingRemovals = heavy ? 0 : state.RoundEnd.RemainingRemovals - 1;
            state.Pending = null;
            RemoveMinion(catalog, state, command, command.Value, "R-ROUND-END", resumeRoundEnd: heavy);
            if (state.Frontline == null) ContinueRoundMinionBattle(catalog, state, command);
        }
        private static void CompleteRoundEnd(GameState state, Command command)
        {
            var progress = state.RoundEnd!;
            foreach (var upgrade in progress.Upgrades)
            {
                var player = state.Players[upgrade.Seat];
                if (player.Level == upgrade.StartingLevel)
                {
                    player.Gold++;
                    Emit(state, command, "RoundCompensationGranted", player.Seat, detail: "1");
                }
            }
            Emit(state, command, "RoundEnded", detail: state.Round.ToString());
            state.Round++; state.Turn = 1; state.RoundEnd = null;
            StartPlanning(state, command);
        }
    }
}

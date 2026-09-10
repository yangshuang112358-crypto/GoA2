#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        public static List<Hex> LegalDeployments(ContentCatalog catalog, GameState state, int actor, int target)
        {
            if (state.Phase != Phase.Deployment || actor < 0 || actor > 3 || target < 0 || target > 3) return new List<Hex>();
            var player = state.Players[target];
            if (actor != Captain(state, player.Team) || state.Units.Any(u => u.Seat == target)) return new List<Hex>();
            string spawn = player.Team == Team.Blue ? "blueHeroSpawn" : "redHeroSpawn";
            return catalog.Cells.Where(c => c.Spawn == spawn && !c.Obstacle && !state.Units.Any(u => u.Position == c.Position)).Select(c => c.Position).ToList();
        }
        private static int Captain(GameState state, Team team) => team == Team.Blue ? state.BlueCaptain : state.RedCaptain;
        private static void DeployHero(ContentCatalog catalog, GameState state, Command command)
        {
            Require(LegalDeployments(catalog, state, command.ActorSeat, command.TargetSeat).Contains(command.Destination), "invalid_deployment", "须由本队队长选择未占用的本队出生点。");
            var player = state.Players[command.TargetSeat];
            state.Units.Add(new UnitState { Id = "hero:" + player.Seat, Kind = "hero", Team = player.Team, Seat = player.Seat, Position = command.Destination });
            Emit(state, command, "HeroDeployed", player.Seat, detail: command.Destination.ToString());
            state.Events.Last().To = command.Destination;
            if (state.Units.Count(u => u.Kind == "hero") == 4) StartPlanning(state, command);
        }
        private static void StartPlanning(GameState state, Command command)
        {
            state.Phase = Phase.Planning; state.ActiveSeat = null; state.Pending = null;
            foreach (var player in state.Players)
            {
                player.Confirmed = !player.Cards.Any(c => c.Zone == CardZone.InHand);
                if (player.Confirmed) Emit(state, command, "EmptyHandSkipped", player.Seat);
            }
            Emit(state, command, "PlanningStarted", detail: state.Round + ":" + state.Turn);
        }
        private static void SelectCard(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.Planning, "wrong_phase", "当前不是暗选阶段。");
            var player = state.Players[command.ActorSeat];
            Require(!player.Confirmed, "selection_locked", "已经确认的选择不能更改。");
            var card = player.Cards.SingleOrDefault(c => c.CardId == command.Value && (c.Zone == CardZone.InHand || c.Zone == CardZone.Selected));
            Require(card != null, "invalid_card", "只能选择自己的可用手牌。");
            foreach (var previous in player.Cards.Where(c => c.Zone == CardZone.Selected)) previous.Zone = CardZone.InHand;
            card!.Zone = CardZone.Selected;
            Emit(state, command, "CardSelected", player.Seat, card.CardId, player.Seat);
            if (state.QuickSelection) TryReveal(catalog, state, command);
        }
        private static void ConfirmCard(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.Planning, "wrong_phase", "当前不是暗选阶段。");
            var player = state.Players[command.ActorSeat];
            Require(!player.Confirmed && player.Cards.Any(c => c.Zone == CardZone.Selected), "nothing_to_confirm", "请先选择一张手牌。");
            player.Confirmed = true;
            Emit(state, command, "SelectionConfirmed", player.Seat);
            TryReveal(catalog, state, command);
        }
        private static void TryReveal(ContentCatalog catalog, GameState state, Command command)
        {
            if (state.Phase != Phase.Planning) return;
            if (state.QuickSelection)
            {
                if (state.Players.Any(p => p.Cards.Any(c => c.Zone == CardZone.InHand || c.Zone == CardZone.Selected) && !p.Cards.Any(c => c.Zone == CardZone.Selected))) return;
                foreach (var player in state.Players)
                {
                    if (!player.Confirmed) Emit(state, command, "SelectionConfirmed", player.Seat);
                    player.Confirmed = true;
                }
            }
            else if (!state.Players.All(p => p.Confirmed)) return;
            foreach (var owner in state.Players)
            {
                foreach (var card in owner.Cards.Where(c => c.Zone == CardZone.Selected))
                {
                    card.Zone = CardZone.PlayedUnresolved; card.PlayedRound = state.Round; card.PlayedTurn = state.Turn;
                    Emit(state, command, "CardRevealed", owner.Seat, card.CardId);
                }
            }
            AdvanceInitiative(catalog, state, command);
        }
        private static void AdvanceInitiative(ContentCatalog catalog, GameState state, Command command)
        {
            state.ActiveSeat = null; state.Pending = null;
            var candidates = state.Players.Where(p => p.Cards.Any(c => c.Zone == CardZone.PlayedUnresolved)).ToList();
            if (candidates.Count == 0)
            {
                EndTurn(catalog, state, command);
                return;
            }
            int initiative = candidates.Max(p => catalog.Card(p.Cards.Single(c => c.Zone == CardZone.PlayedUnresolved).CardId).Initiative + p.InitiativeBonus);
            candidates = candidates.Where(p => catalog.Card(p.Cards.Single(c => c.Zone == CardZone.PlayedUnresolved).CardId).Initiative + p.InitiativeBonus == initiative).ToList();
            if (candidates.Select(p => p.Team).Distinct().Count() > 1)
            {
                var winningTeam = state.DecisionCoin;
                candidates = candidates.Where(p => p.Team == winningTeam).ToList();
                state.DecisionCoin = winningTeam == Team.Blue ? Team.Red : Team.Blue;
                Emit(state, command, "DecisionCoinFlipped", detail: winningTeam + " wins; now " + state.DecisionCoin);
            }
            if (candidates.Count == 1) BeginAction(catalog, state, command, candidates[0].Seat);
            else
            {
                state.Phase = Phase.InitiativeChoice;
                state.Pending = new PendingChoice
                {
                    Id = state.Round + ":" + state.Turn + ":" + (state.Events.Count + 1), Kind = "initiative",
                    ChooserSeat = Captain(state, candidates[0].Team), CandidateSeats = candidates.Select(p => p.Seat).OrderBy(s => s).ToList(),
                    Source = "R-INITIATIVE", ResumeAt = "begin_action", Optional = false
                };
                Emit(state, command, "InitiativeChoiceRequired", state.Pending.ChooserSeat);
            }
        }
        private static void ChooseInitiative(ContentCatalog catalog, GameState state, Command command)
        {
            var pending = state.Pending;
            Require(state.Phase == Phase.InitiativeChoice && pending != null && pending.ChooserSeat == command.ActorSeat && pending.CandidateSeats.Contains(command.TargetSeat),
                "invalid_initiative_choice", "只能由指定队长选择并列的本队席位。");
            Emit(state, command, "InitiativeChosen", command.TargetSeat);
            BeginAction(catalog, state, command, command.TargetSeat);
        }
        private static void BeginAction(ContentCatalog catalog, GameState state, Command command, int seat)
        {
            state.Pending = null; state.ActiveSeat = seat; state.Phase = Phase.Action;
            if (state.Players[seat].AwaitingRespawn)
            {
                state.Phase = Phase.EffectChoice;
                state.Pending = new PendingChoice { Id = "respawn:" + (state.Events.Count + 1), Kind = "hero_respawn", ChooserSeat = seat, UnitId = "hero:" + seat, Source = "R-DEFEAT", ResumeAt = "begin_action" };
                state.Pending.CandidateCells = LegalRespawns(catalog, state, seat);
                Emit(state, command, "HeroRespawnChoiceRequired", seat, ActiveCard(state).CardId);
                return;
            }
            Emit(state, command, "ActionStarted", seat, ActiveCard(state).CardId);
        }
        private static CardInstance ActiveCard(GameState state) => state.Players[state.ActiveSeat!.Value].Cards.Single(c => c.Zone == CardZone.PlayedUnresolved);
        private static void FinishAction(ContentCatalog catalog, GameState state, Command command)
        {
            var card = ActiveCard(state);
            card.Zone = CardZone.PlayedResolved;
            Emit(state, command, "CardResolved", state.ActiveSeat, card.CardId);
            AdvanceInitiative(catalog, state, command);
        }
        private static void EndTurn(ContentCatalog catalog, GameState state, Command command)
        {
            Emit(state, command, "TurnEnded", detail: state.Round + ":" + state.Turn);
            if (state.Turn == catalog.Rules.TurnsPerRound)
            {
                state.Phase = Phase.RoundEnd;
                Emit(state, command, "RoundEndReached", detail: "本切片到达轮末；轮末结算与升级尚未实装。");
                return;
            }
            state.Turn++;
            StartPlanning(state, command);
            if (state.Players.All(p => p.Confirmed)) EndTurn(catalog, state, command);
        }
    }
}

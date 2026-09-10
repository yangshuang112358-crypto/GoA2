#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        // Sandbox is fixed when a local match is created. Every debug intent is journaled
        // through the same atomic command boundary as a normal action.
        private static void ApplyDebug(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Sandbox, "debug_disabled", "普通对局不能使用调试命令。");
            switch (command.Kind)
            {
                case CommandKind.SetQuickSelection:
                    Require(command.Value == "on" || command.Value == "off", "invalid_mode", "模式应为on或off。");
                    state.QuickSelection = command.Value == "on";
                    Emit(state, command, "QuickSelectionChanged", detail: command.Value);
                    if (state.QuickSelection && state.Phase == Phase.Planning)
                    {
                        foreach (var player in state.Players) player.Confirmed = !player.Cards.Any(c => c.Zone == CardZone.InHand || c.Zone == CardZone.Selected);
                        TryReveal(catalog, state, command);
                    }
                    break;
                case CommandKind.DebugGold:
                case CommandKind.DebugSetGold:
                    var recipient = DebugPlayer(state, command.TargetSeat);
                    Require(int.TryParse(command.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount), "invalid_amount", "请输入整数金币变化量。");
                    long total = command.Kind == CommandKind.DebugSetGold ? amount : (long)recipient.Gold + amount;
                    Require(total >= 0 && total <= 9999, "invalid_amount", "调试金币结果须在0至9999之间。");
                    recipient.Gold = (int)total;
                    Emit(state, command, command.Kind == CommandKind.DebugSetGold ? "DebugGoldSet" : "DebugGoldChanged", recipient.Seat, detail: amount.ToString(CultureInfo.InvariantCulture));
                    break;
                case CommandKind.DebugTeleport:
                    Require(LegalDebugTeleports(catalog, state, command.Value).Contains(command.Destination), "invalid_teleport", "请选择存在、无障碍且未占用的地图格。");
                    var unit = state.Units.Single(u => u.Id == command.Value);
                    var origin = unit.Position; unit.Position = command.Destination;
                    Emit(state, command, "DebugTeleported", unit.Seat, detail: unit.Id);
                    state.Events.Last().From = origin; state.Events.Last().To = unit.Position;
                    if (state.Frontline != null) ContinueFrontline(catalog, state, command);
                    break;
                case CommandKind.DebugRemoveMinion:
                case CommandKind.DebugDefeatMinion:
                    Require(state.Phase != Phase.HeroSelection && state.Phase != Phase.Deployment, "wrong_phase", "请先完成初始准备。");
                    RemoveMinion(catalog, state, command, command.Value, "debug",
                        command.Kind == CommandKind.DebugDefeatMinion ? (int?)DebugPlayer(state, command.TargetSeat).Seat : null, true);
                    break;
                case CommandKind.DebugSetCrystal:
                    Require(state.Phase != Phase.HeroSelection && state.Phase != Phase.Deployment, "wrong_phase", "请先完成初始准备。");
                    var team = DebugPlayer(state, command.TargetSeat).Team;
                    Require(int.TryParse(command.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int life) && life >= 0 && life <= 99, "invalid_amount", "水晶生命须为0至99的整数。");
                    if (team == Team.Blue) state.BlueCrystal = life; else state.RedCrystal = life;
                    Emit(state, command, "DebugCrystalSet", detail: team + ":" + life);
                    if (life == 0) DeclareVictory(state, command, OtherTeam(team), "crystal");
                    break;
                case CommandKind.DebugDiscard: DebugDiscard(catalog, state, command); break;
                case CommandKind.DebugRecover: DebugRecover(catalog, state, command); break;
                case CommandKind.DebugPrepare: DebugPrepare(catalog, state, command); break;
                case CommandKind.DebugSelectAll: DebugSelectAll(catalog, state, command); break;
                case CommandKind.DebugEquipCard: DebugEquip(catalog, state, command); break;
                case CommandKind.DebugConfirmAll: DebugConfirmAll(catalog, state, command); break;
                case CommandKind.DebugAdvance: DebugAdvance(catalog, state, command); break;
                case CommandKind.DebugSetCoin:
                    Require(command.Value == "blue" || command.Value == "red", "invalid_team", "决策币只能为蓝或红。");
                    state.DecisionCoin = command.Value == "blue" ? Team.Blue : Team.Red;
                    Emit(state, command, "DebugCoinChanged", detail: command.Value);
                    break;
            }
        }
        private static PlayerState DebugPlayer(GameState state, int seat)
        {
            Require(seat >= 0 && seat < 4, "invalid_target_seat", "调试目标席位不存在。");
            return state.Players[seat];
        }
        public static List<Hex> LegalDebugTeleports(ContentCatalog catalog, GameState state, string unitId)
        {
            if (!state.Sandbox || state.Phase == Phase.Finished || !state.Units.Any(u => u.Id == unitId)) return new List<Hex>();
            var occupied = new HashSet<Hex>(state.Units.Select(u => u.Position));
            return catalog.Cells.Where(c => !c.Obstacle && !occupied.Contains(c.Position)).OrderBy(c => c.Position.X).ThenBy(c => c.Position.Y).Select(c => c.Position).ToList();
        }
        private static Command AsActor(Command original, int actor, string value = "", int target = -1, Hex destination = default) =>
            new Command { Id = original.Id, MatchId = original.MatchId, ExpectedRevision = original.ExpectedRevision, Kind = original.Kind, ActorSeat = actor, Value = value, TargetSeat = target, Destination = destination };
        private static void DebugPrepare(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.HeroSelection || state.Phase == Phase.Deployment, "wrong_phase", "自动准备只在选英雄或出生阶段可用。");
            var requested = string.IsNullOrWhiteSpace(command.Value) ? Array.Empty<string>() : command.Value.Split(',');
            Require(requested.Length == 0 || (requested.Length == 4 && requested.Distinct().Count() == 4 && requested.All(id => catalog.Heroes.Any(h => h.Id == id))), "invalid_heroes", "指定英雄时需要四个不重复的正式英雄ID。");
            if (state.Phase == Phase.HeroSelection)
            {
                foreach (var player in state.Players)
                {
                    if (player.HeroId != null) continue;
                    string hero = requested.Length == 4 ? requested[player.Seat] : catalog.Heroes.First(h => state.Players.All(p => p.HeroId != h.Id)).Id;
                    ChooseHero(catalog, state, AsActor(command, player.Seat, hero));
                }
            }
            foreach (var player in state.Players)
            {
                if (state.Units.Any(u => u.Seat == player.Seat)) continue;
                int captain = Captain(state, player.Team);
                var targets = LegalDeployments(catalog, state, captain, player.Seat).OrderBy(h => h.X).ThenBy(h => h.Y).ToList();
                Require(targets.Count > 0, "no_spawn", "当前英雄没有合法出生点，自动准备未应用。");
                DeployHero(catalog, state, AsActor(command, captain, target: player.Seat, destination: targets[0]));
            }
            Emit(state, command, "DebugPrepared");
        }
        private static void DebugSelectAll(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.Planning, "wrong_phase", "快速选牌只在暗选阶段可用。");
            Require(command.Value == "" || command.Value == "first" || command.Value == "highest" || command.Value == "random", "invalid_strategy", "选牌策略无效。");
            foreach (var player in state.Players)
            {
                if (state.Phase != Phase.Planning) break;
                if (player.Confirmed) continue;
                var choices = player.Cards.Where(c => c.Zone == CardZone.InHand || c.Zone == CardZone.Selected).ToList();
                if (choices.Count == 0) continue;
                var card = choices[0];
                if (command.Value == "highest") card = choices.OrderByDescending(c => catalog.Card(c.CardId).Initiative).ThenBy(c => c.CardId, StringComparer.Ordinal).First();
                if (command.Value == "random")
                {
                    uint sample = unchecked((uint)state.Seed ^ (uint)state.Revision * 747796405u ^ (uint)(player.Seat + 1) * 2891336453u);
                    sample ^= sample << 13; sample ^= sample >> 17; sample ^= sample << 5;
                    card = choices[(int)(sample % choices.Count)];
                }
                SelectCard(catalog, state, AsActor(command, player.Seat, card.CardId));
            }
        }
        private static void DebugDiscard(ContentCatalog catalog, GameState state, Command command)
        {
            var player = DebugPlayer(state, command.TargetSeat);
            var card = player.Cards.SingleOrDefault(c => c.CardId == command.Value && (c.Zone == CardZone.InHand || c.Zone == CardZone.Selected));
            Require(card != null && !(card.Zone == CardZone.Selected && player.Confirmed), "invalid_discard", "只能调试弃置未锁定的手牌。");
            card!.Zone = CardZone.Discarded;
            Emit(state, command, "CardDiscarded", player.Seat, card.CardId, player.Seat);
            Emit(state, command, "DiscardColorShown", player.Seat, detail: catalog.Card(card.CardId).Color);
            if (state.Phase == Phase.Planning)
            {
                if (!player.Cards.Any(c => c.Zone == CardZone.InHand || c.Zone == CardZone.Selected))
                {
                    player.Confirmed = true; Emit(state, command, "EmptyHandSkipped", player.Seat);
                }
                TryReveal(catalog, state, command);
            }
        }
        private static void DebugRecover(ContentCatalog catalog, GameState state, Command command)
        {
            var player = DebugPlayer(state, command.TargetSeat);
            var card = player.Cards.SingleOrDefault(c => c.CardId == command.Value && c.Zone == CardZone.Discarded);
            Require(card != null, "invalid_recovery", "只能取回本人的弃牌。");
            bool wasEmpty = !player.Cards.Any(c => c.Zone == CardZone.InHand || c.Zone == CardZone.Selected);
            card!.Zone = CardZone.InHand;
            if (wasEmpty && state.Phase == Phase.Planning) player.Confirmed = false;
            Emit(state, command, "CardRecovered", player.Seat, card.CardId, player.Seat);
            Emit(state, command, "RecoveredColorShown", player.Seat, detail: catalog.Card(card.CardId).Color);
        }
        private static void DebugEquip(ContentCatalog catalog, GameState state, Command command)
        {
            var player = DebugPlayer(state, command.TargetSeat);
            var definition = catalog.Cards.FirstOrDefault(c => c.Id == command.Value && c.HeroId == player.HeroId && c.Color != "purple");
            Require(definition != null, "invalid_equipment", "只能装配本英雄的非紫卡。");
            Require(state.Phase == Phase.Planning && !player.Confirmed, "wrong_phase", "请在未确认的选牌阶段装配测试牌。");
            var existing = player.Cards.SingleOrDefault(c => catalog.Card(c.CardId).Color == definition!.Color);
            Require(existing != null && existing.Zone != CardZone.PlayedUnresolved && existing.Zone != CardZone.Selected, "invalid_equipment", "该颜色当前不可替换。");
            existing!.CardId = definition!.Id; existing.Zone = CardZone.InHand; existing.PlayedRound = null; existing.PlayedTurn = null;
            Emit(state, command, "DebugCardEquipped", player.Seat, definition.Id, player.Seat);
        }
        private static void DebugConfirmAll(ContentCatalog catalog, GameState state, Command command)
        {
            Require(state.Phase == Phase.Planning, "wrong_phase", "只能在选牌阶段确认。");
            Require(state.Players.All(p => p.Confirmed || p.Cards.Any(c => c.Zone == CardZone.Selected)), "missing_selection", "请先为所有有手牌的玩家选择卡牌。");
            foreach (var player in state.Players)
                if (!player.Confirmed && state.Phase == Phase.Planning) ConfirmCard(catalog, state, AsActor(command, player.Seat));
        }
        private static void DebugAdvance(ContentCatalog catalog, GameState state, Command command)
        {
            Require(command.Value == "action" || command.Value == "turn" || command.Value == "round", "invalid_boundary", "请选择当前行动、当前回合或轮末。");
            Require(state.Phase == Phase.Action || state.Phase == Phase.InitiativeChoice || (state.Phase == Phase.Planning && command.Value != "action"), "wrong_phase", "当前阶段不能快进；请先处理待选择事项。");
            Require(state.Pending == null || (state.Phase == Phase.InitiativeChoice && state.Pending.Kind == "initiative"), "wrong_phase", "请先处理当前强制选择，快进不会代选卡牌效果。");
            int round = state.Round, turn = state.Turn, actions = 0;
            Emit(state, command, "DebugAdvanceStarted", detail: command.Value);
            for (int step = 0; step < 128; step++)
            {
                if (state.Round != round || state.Phase == Phase.RoundEnd || (command.Value == "turn" && state.Turn != turn) || (command.Value == "action" && actions == 1)) break;
                if (state.Phase == Phase.Planning)
                {
                    DebugSelectAll(catalog, state, AsActor(command, command.ActorSeat, "first"));
                    if (state.Phase == Phase.Planning) DebugConfirmAll(catalog, state, command);
                }
                else if (state.Phase == Phase.InitiativeChoice && state.Pending?.Kind == "initiative")
                {
                    ChooseInitiative(state, AsActor(command, state.Pending.ChooserSeat, target: state.Pending.CandidateSeats.Min()));
                }
                else if (state.Phase == Phase.Action && state.Pending == null)
                {
                    var actor = AsActor(command, state.ActiveSeat!.Value);
                    Emit(state, actor, "ActionPassed", actor.ActorSeat, ActiveCard(state).CardId);
                    FinishAction(catalog, state, actor); actions++;
                }
                else break;
                Require(step < 127, "advance_limit", "快进达到上限，操作未应用。");
            }
            Emit(state, command, "DebugAdvanceFinished", detail: command.Value + ":" + state.Phase);
        }
    }
}

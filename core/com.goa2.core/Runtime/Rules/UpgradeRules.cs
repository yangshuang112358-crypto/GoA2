#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        public static List<UpgradeOption> LegalUpgrades(ContentCatalog catalog, GameState state, int seat)
        {
            var options = new List<UpgradeOption>();
            if (seat < 0 || seat >= state.Players.Count || state.Phase != Phase.RoundEnd || state.RoundEnd?.Stage != "upgrades") return options;
            var progress = state.RoundEnd.Upgrades.SingleOrDefault(p => p.Seat == seat);
            if (progress == null || progress.PendingLevels.Count == 0) return options;
            var player = state.Players[seat];
            if (progress.PendingLevels[0] == 8)
            {
                if (state.EngineVersion >= 9)
                    options.Add(new UpgradeOption { CardId = catalog.Cards.Single(c => c.HeroId == player.HeroId && c.Color == "purple").Id, Color = "purple", CardLevel = 4, HeroLevel = 8 });
                return options;
            }
            string[] colors = { "red", "green", "blue" };
            var levels = colors.ToDictionary(color => color, color => player.UpgradeHistory.Where(h => h.Color == color).Select(h => h.CardLevel).DefaultIfEmpty(1).Max());
            int eligible = levels.Values.Any(l => l == 1) ? 1 : 2;
            foreach (string color in colors.Where(c => levels[c] == eligible))
            {
                var current = player.Cards.Single(c => catalog.Card(c.CardId).Color == color);
                var pair = catalog.Cards.Where(c => c.HeroId == player.HeroId && c.Color == color && c.Level == eligible + 1).OrderBy(c => c.Id, System.StringComparer.Ordinal).ToList();
                Require(pair.Count == 2, "invalid_upgrade_catalog", "正式升级候选必须为同色同级两张。");
                foreach (var chosen in pair)
                {
                    var rejected = pair.Single(c => c.Id != chosen.Id);
                    options.Add(new UpgradeOption
                    {
                        CardId = chosen.Id, PreviousCardId = current.CardId, RejectedCardId = rejected.Id, Bonus = rejected.Passive ?? "",
                        Color = color, CardLevel = eligible + 1, HeroLevel = progress.PendingLevels[0]
                    });
                }
            }
            return options;
        }
        private static void BeginUpgrades(ContentCatalog catalog, GameState state, Command command)
        {
            var progress = state.RoundEnd!;
            Require(progress.Stage == "minion_battle", "invalid_round_end", "升级费用已经结算。");
            progress.Stage = "upgrades"; state.Phase = Phase.RoundEnd;
            foreach (var player in state.Players)
            {
                var upgrade = new PlayerUpgradeProgress { Seat = player.Seat, StartingLevel = player.Level };
                while (player.Level < 8 && player.Gold >= player.Level)
                {
                    int cost = player.Level;
                    player.Gold -= cost; player.Level++;
                    upgrade.PendingLevels.Add(player.Level);
                    Emit(state, command, "HeroLeveled", player.Seat, detail: cost + ":" + player.Level + ":" + cost);
                }
                progress.Upgrades.Add(upgrade);
            }
            Emit(state, command, "UpgradesStarted", detail: state.Round.ToString());
            foreach (var upgrade in progress.Upgrades) ContinuePlayerUpgrade(catalog, state, command, upgrade);
            if (progress.Upgrades.All(p => p.PendingLevels.Count == 0)) CompleteRoundEnd(state, command);
        }
        private static void ContinuePlayerUpgrade(ContentCatalog catalog, GameState state, Command command, PlayerUpgradeProgress progress)
        {
            if (state.EngineVersion < 9 && progress.PendingLevels.Count > 0 && progress.PendingLevels[0] == 8)
            {
                var player = state.Players[progress.Seat];
                var purple = catalog.Cards.Single(c => c.HeroId == player.HeroId && c.Color == "purple" && c.Level == 4);
                player.PurpleCardId = purple.Id; progress.PendingLevels.RemoveAt(0);
                Emit(state, command, "PurpleCardGranted", player.Seat, purple.Id);
            }
            if (progress.PendingLevels.Count > 0) Emit(state, command, "UpgradeChoiceRequired", progress.Seat, privateTo: progress.Seat, detail: progress.PendingLevels[0].ToString());
        }
        private static void ChooseUpgrade(ContentCatalog catalog, GameState state, Command command)
        {
            var option = LegalUpgrades(catalog, state, command.ActorSeat).SingleOrDefault(o => o.CardId == command.Value);
            Require(option != null, "invalid_upgrade", "请选择本人的当前合法升级候选。");
            var player = state.Players[command.ActorSeat];
            if (option!.Color == "purple")
            {
                player.PurpleCardId = option.CardId;
                Emit(state, command, "PurpleCardGranted", player.Seat, option.CardId);
            }
            else
            {
            var card = player.Cards.Single(c => c.CardId == option!.PreviousCardId);
            card.CardId = option!.CardId; card.Zone = CardZone.InHand; card.PlayedRound = null; card.PlayedTurn = null;
            ApplyPermanentBonus(player, option.Bonus);
            player.UpgradeHistory.Add(new UpgradeRecord
            {
                Round = state.Round, HeroLevel = option.HeroLevel, CardLevel = option.CardLevel, Color = option.Color,
                PreviousCardId = option.PreviousCardId, SelectedCardId = option.CardId, RejectedCardId = option.RejectedCardId, Bonus = option.Bonus
            });
            Emit(state, command, "CardUpgraded", player.Seat, option.CardId, player.Seat, option.PreviousCardId);
            Emit(state, command, "UpgradeBonusGranted", player.Seat, option.RejectedCardId, player.Seat, option.Bonus + ":1");
            }
            var progress = state.RoundEnd!.Upgrades.Single(p => p.Seat == player.Seat);
            progress.PendingLevels.RemoveAt(0);
            ContinuePlayerUpgrade(catalog, state, command, progress);
            if (state.RoundEnd.Upgrades.All(p => p.PendingLevels.Count == 0)) CompleteRoundEnd(state, command);
        }
        private static void ApplyPermanentBonus(PlayerState player, string bonus)
        {
            switch (bonus)
            {
                case "攻击": player.AttackBonus++; break;
                case "防御": player.DefenseBonus++; break;
                case "移动": player.MovementBonus++; break;
                case "先攻": player.InitiativeBonus++; break;
                case "范围": player.RangeBonus++; break;
                case "远程": player.RangedBonus++; break;
                default: throw new RuleViolation("invalid_upgrade_bonus", "未选候选的被动图标不受支持。");
            }
        }
    }
}

#nullable enable
using System;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        public GameState Create(ContentCatalog catalog, string matchId, string[] names, int seed, int engineVersion = GameState.CurrentEngineVersion)
        {
            Require(engineVersion >= 0 && engineVersion <= GameState.CurrentEngineVersion, "incompatible_engine", "规则引擎版本不兼容。");
            Require(names.Length == 4 && names.All(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 40), "invalid_players", "需要四个有效玩家名称。");
            Require(!string.IsNullOrWhiteSpace(matchId) && matchId.Length <= 100, "invalid_match", "对局编号无效。");
            return new GameState
            {
                MatchId = matchId, ContentVersion = catalog.Version, ContentHash = catalog.Hash,
                InitialEngineVersion = engineVersion, EngineVersion = engineVersion,
                RulesVersion = catalog.Rules.Version, Seed = seed, DecisionCoin = (seed & 1) == 0 ? Team.Blue : Team.Red,
                BlueCrystal = catalog.Rules.StartingCrystalLife, RedCrystal = catalog.Rules.StartingCrystalLife,
                VictoryMarksRequired = catalog.Rules.FrontlineVictoryMarks, CombatRegion = catalog.Rules.InitialCombatRegion,
                Players = names.Select((name, seat) => new PlayerState { Seat = seat, Team = seat % 2 == 0 ? Team.Blue : Team.Red, Name = name }).ToList()
            };
        }
        public void Apply(ContentCatalog catalog, GameState state, Command command)
        {
            Require(command.ActorSeat >= 0 && command.ActorSeat < 4, "invalid_actor", "无效席位。");
            Require(state.Phase != Phase.Finished, "match_finished", "本局已经结束，请创建新对局。");
            switch (command.Kind)
            {
                case CommandKind.ChooseHero: ChooseHero(catalog, state, command); break;
                case CommandKind.DeployHero: DeployHero(catalog, state, command); break;
                case CommandKind.SelectCard: SelectCard(catalog, state, command); break;
                case CommandKind.ConfirmCard: ConfirmCard(catalog, state, command); break;
                case CommandKind.ChooseInitiative: ChooseInitiative(catalog, state, command); break;
                case CommandKind.ChooseMinionSpawn: ChooseMinionSpawn(catalog, state, command); break;
                case CommandKind.ChooseMinionProtection: ChooseMinionProtection(catalog,state,command); break;
                case CommandKind.ChoosePrimaryOption: ChoosePrimaryOption(catalog,state,command); break;
                case CommandKind.BeginPrimary: BeginPrimary(catalog, state, command); break;
                case CommandKind.ChooseDiscardAttack: ChooseDiscardAttack(catalog,state,command); break;
                case CommandKind.ChooseAttackTarget: ChooseAttackTarget(catalog, state, command); break;
                case CommandKind.Defend: Defend(catalog, state, command); break;
                case CommandKind.DeclineDefense: DeclineDefense(catalog, state, command); break;
                case CommandKind.ForcedDiscard: ForcedDiscard(catalog, state, command); break;
                case CommandKind.DeclineRetaliationDiscard: DeclineRetaliationDiscard(catalog,state,command); break;
                case CommandKind.ChooseOptionalDiscard: ChooseOptionalDiscard(catalog,state,command); break;
                case CommandKind.ChooseEffectMove: ChooseEffectMove(catalog,state,command); break;
                case CommandKind.ChooseMinionReturn: ChooseMinionReturn(catalog,state,command); break;
                case CommandKind.ChoosePlacement: ChoosePlacement(catalog,state,command); break;
                case CommandKind.ChooseRecoveredCard: ChooseRecoveredCard(catalog,state,command); break;
                case CommandKind.ChooseEffectTarget: ChooseEffectTarget(catalog,state,command); break;
                case CommandKind.ChooseGoldTransfer: ChooseGoldTransfer(catalog,state,command); break;
                case CommandKind.ChooseCardSwap: ChooseCardSwap(catalog,state,command); break;
                case CommandKind.RespawnHero: RespawnHero(catalog, state, command); break;
                case CommandKind.ResolveRoundEnd: ResolveRoundEnd(catalog, state, command); break;
                case CommandKind.ChooseRoundMinionRemoval: ChooseRoundMinionRemoval(catalog, state, command); break;
                case CommandKind.ChooseUpgrade: ChooseUpgrade(catalog, state, command); break;
                case CommandKind.UpgradeEngine: UpgradeEngine(state, command); break;
                case CommandKind.Move: Move(catalog, state, command); break;
                case CommandKind.Pass:
                    Require(state.Phase == Phase.Action && state.ActiveSeat == command.ActorSeat && state.Pending == null && state.Execution == null, "not_active", "当前不由你行动，或仍有待处理选择。");
                    Emit(state, command, "ActionPassed", command.ActorSeat, ActiveCard(state).CardId);
                    FinishAction(catalog, state, command);
                    break;
                case CommandKind.SetQuickSelection:
                case CommandKind.DebugGold:
                case CommandKind.DebugTeleport:
                case CommandKind.DebugDiscard:
                case CommandKind.DebugRecover:
                case CommandKind.DebugPrepare:
                case CommandKind.DebugSelectAll:
                case CommandKind.DebugEquipCard:
                case CommandKind.DebugSetCoin:
                case CommandKind.DebugConfirmAll:
                case CommandKind.DebugAdvance:
                case CommandKind.DebugSetGold:
                case CommandKind.DebugRemoveMinion:
                case CommandKind.DebugDefeatMinion:
                case CommandKind.DebugSetCrystal:
                case CommandKind.DebugDefeatHero:
                case CommandKind.DebugAttack:
                    ApplyDebug(catalog, state, command); break;
                default: throw new RuleViolation("unsupported_command", "此操作尚未实装。");
            }
            if(command.Kind==CommandKind.DebugDiscard)BeginDiscardReactions(catalog,state,command,"command:"+command.Id);
        }
        private static void Move(ContentCatalog catalog, GameState state, Command command, bool allowBeforeAction = true)
        {
            if(state.EngineVersion>=58 && command.Value=="begin")
            {
                Require(CanBeginMovementPrelude(catalog,state,command.ActorSeat,command.MoveMode),"invalid_move","当前不能开始行动前移动。");
                BeginBeforeAction(catalog,state,command);return;
            }
            var option = MovementRules.LegalMoves(catalog, state, command.ActorSeat, command.MoveMode).FirstOrDefault(o => o.Destination == command.Destination);
            Require(option != null, "invalid_move", "目标不在当前合法移动集合中。");
            if(allowBeforeAction && BeginBeforeAction(catalog,state,command))return;
            var unit = state.Units.Single(u => u.Seat == command.ActorSeat);
            var origin = unit.Position;
            unit.Position = command.Destination;
            Emit(state, command, "UnitMoved", command.ActorSeat, ActiveCard(state).CardId, detail: command.MoveMode.ToString());
            var moved = state.Events.Last();
            moved.From = origin; moved.To = command.Destination; moved.Path = option!.Path;
            FinishAction(catalog, state, command);
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

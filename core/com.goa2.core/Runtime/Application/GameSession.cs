#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Domain;
using Goa2.Rules;

namespace Goa2.Application
{
    // Only the host owns this service. A network adapter must bind authenticatedSeat
    // from its connection, never from a client's command or hotseat selector.
    public sealed class GameSession
    {
        private readonly ContentCatalog catalog;
        private readonly IStateCodec codec;
        private readonly GameRules rules = new GameRules();
        private GameState state;
        private readonly object gate = new object();

        public GameSession(ContentCatalog catalog, IStateCodec codec, GameState initial)
        {
            this.catalog = ContentSnapshot.Copy(catalog); this.codec = codec;
            state = codec.Read(codec.Write(initial));
            ValidateVersion();
        }
        private void ValidateVersion()
        {
            if (state.EngineVersion < 0 || state.EngineVersion > GameState.CurrentEngineVersion || state.InitialEngineVersion < 0 || state.InitialEngineVersion > state.EngineVersion)
                throw new RuleViolation("incompatible_engine", "规则引擎版本不兼容。");
            if (state.ProtocolVersion != GameState.CurrentProtocol || state.ContentHash != catalog.Hash ||
                state.ContentVersion != catalog.Version || state.RulesVersion != catalog.Rules.Version)
                throw new RuleViolation("incompatible_save", "存档协议、规则或内容版本不匹配。");
        }
        public string ExportSave() { lock (gate) return codec.Write(state); }
        public GameView View(int? seat) { lock (gate) return Project(seat); }

        // The host supplies a canonical fresh origin. Saved files enter through
        // LocalGameFactory.Restore, which also verifies the complete derived state.
        public static GameSession Replay(ContentCatalog catalog,IStateCodec codec,GameState initial,System.Collections.Generic.IReadOnlyList<Command> commands)
        {
            if(commands==null || commands.Count>10000) throw new RuleViolation("invalid_replay","重放命令数量无效。");
            var replay=new GameSession(catalog,codec,initial);
            if(replay.state.Revision!=0 || replay.state.Receipts.Count!=0 || replay.state.AcceptedCommands.Count!=0)
                throw new RuleViolation("invalid_replay_origin","重放须从新的规范初始状态开始。");
            // This session is private until the complete import succeeds. Any rejected
            // command abandons it, so intermediate copies and player views are unnecessary.
            foreach(var command in commands)
            {
                var result=replay.ExecuteCore(command.ActorSeat,command,true);
                if(!result.Accepted || result.Duplicate)
                    throw new RuleViolation("invalid_replay","存档命令无法重放："+result.Code);
            }
            return replay;
        }

        public CommandResult Execute(int authenticatedSeat, Command command)
        {
            lock (gate) return ExecuteCore(authenticatedSeat,command,false);
        }
        private CommandResult ExecuteCore(int authenticatedSeat,Command command,bool replay)
        {
            if (authenticatedSeat < 0 || authenticatedSeat > 3 || command.ActorSeat != authenticatedSeat)
                return Reject("unauthorized", "连接身份与操作席位不符。", authenticatedSeat,replay);
            if (command.MatchId != state.MatchId) return Reject("wrong_match", "命令不属于本局。", authenticatedSeat,replay);
            if (string.IsNullOrWhiteSpace(command.Id) || command.Id.Length > 100)
                return Reject("invalid_command_id", "命令编号无效。", authenticatedSeat,replay);
            string fingerprint = Fingerprint(codec.WriteCommand(command));
            var previous = state.Receipts.FirstOrDefault(r => r.Id == command.Id);
            if (previous != null)
            {
                if (previous.ActorSeat != authenticatedSeat || previous.Fingerprint != fingerprint)
                    return Reject("command_id_conflict", "命令编号已被其他内容使用。", authenticatedSeat,replay);
                return new CommandResult { Accepted = true, Duplicate = true, Code = "duplicate", View = ResultView(authenticatedSeat,replay) };
            }
            if (command.ExpectedRevision != state.Revision) return Reject("stale_revision", "状态已更新，请重新选择。", authenticatedSeat,replay);
            var draft = replay ? state : codec.Read(codec.Write(state));
            try { rules.Apply(catalog, draft, command); }
            catch (RuleViolation error) { return Reject(error.Code, error.Message, authenticatedSeat,replay); }
            draft.Revision++;
            draft.Receipts.Add(new CommandReceipt { Id = command.Id, ActorSeat = authenticatedSeat, Fingerprint = fingerprint, Revision = draft.Revision });
            draft.AcceptedCommands.Add(new Command
            {
                Id = command.Id, MatchId = command.MatchId, ExpectedRevision = command.ExpectedRevision, ActorSeat = command.ActorSeat,
                Kind = command.Kind, Value = command.Value, TargetSeat = command.TargetSeat, Destination = command.Destination, MoveMode = command.MoveMode
            });
            state = draft;
            return new CommandResult { Accepted = true, Code = "ok", View = ResultView(authenticatedSeat,replay) };
        }
        private GameView ResultView(int? seat,bool replay) => replay ? new GameView() : Project(seat);
        private CommandResult Reject(string code, string message, int seat,bool replay) =>
            new CommandResult { Code = code, Message = message, View = ResultView(seat >= 0 && seat < 4 ? (int?)seat : null,replay) };
        private GameView Project(int? seat)
        {
            // Build from an independent snapshot, so no returned collection can mutate authority.
            var snapshot = codec.Read(codec.Write(state));
            var view = new GameView
            {
                MatchId = snapshot.MatchId, Revision = snapshot.Revision, Phase = snapshot.Phase, Round = snapshot.Round, Turn = snapshot.Turn,
                Sandbox = snapshot.Sandbox, QuickSelection = snapshot.QuickSelection,
                DecisionCoin = snapshot.DecisionCoin, ActiveSeat = snapshot.ActiveSeat, BlueCaptain = snapshot.BlueCaptain, RedCaptain = snapshot.RedCaptain,
                BlueCrystal = snapshot.BlueCrystal, RedCrystal = snapshot.RedCrystal, CombatRegion = snapshot.CombatRegion,
                BlueMarks = snapshot.BlueMarks, RedMarks = snapshot.RedMarks, VictoryMarksRequired = snapshot.VictoryMarksRequired,
                Winner = snapshot.Winner, VictoryReason = snapshot.VictoryReason, RemovableMinions = GameRules.LegalMinionRemovals(snapshot),
                PendingSpawn = snapshot.Frontline?.Remaining.FirstOrDefault(s => s.Unit.Id == snapshot.Pending?.UnitId)?.Unit,
                PendingSpawns = snapshot.Frontline?.Remaining.Select(s => s.Unit).ToList() ?? new System.Collections.Generic.List<UnitState>(),
                EngineVersion = snapshot.EngineVersion,
                CanUpgradeEngine = seat.HasValue && GameRules.CanUpgradeEngine(snapshot),
                Attack = snapshot.Execution?.Attack,
                AttackRange = CombatRules.CurrentAttackRange(catalog,snapshot),
                RoundEndStage = snapshot.RoundEnd?.Stage ?? "",
                RemainingMinionRemovals = snapshot.RoundEnd?.RemainingRemovals ?? 0,
                UpgradingSeats = snapshot.RoundEnd?.Upgrades.Where(p => p.PendingLevels.Count > 0).Select(p => p.Seat).ToList() ?? new System.Collections.Generic.List<int>(),
                Effects = snapshot.Effects,
                EffectAreas = snapshot.Effects.ToDictionary(e => e.Id, e => EffectRules.Area(catalog,snapshot,e)),
                SupportedPrimaryCards = catalog.Cards.Where(c => CombatRules.HasPrimaryProgram(c,snapshot.EngineVersion)).Select(c => c.Id).ToList(),
                SupportedDefenseCards = catalog.Cards.Where(c => CombatRules.HasDefenseProgram(c,snapshot.EngineVersion)).Select(c => c.Id).ToList(),
                SupportedUltimateCards = catalog.Cards.Where(c => UltimateRules.HasProgram(c,snapshot.EngineVersion)).Select(c => c.Id).ToList(),
                Units = snapshot.Units, Pending = snapshot.Pending,
                Players = snapshot.Players.Select(p => new PlayerView
                {
                    Seat = p.Seat, Team = p.Team, Name = p.Name, HeroId = p.HeroId, Level = p.Level, Gold = p.Gold, Confirmed = p.Confirmed,
                    AwaitingRespawn = p.AwaitingRespawn,
                    PurpleCardId = p.PurpleCardId,
                    PermanentBonuses = new System.Collections.Generic.Dictionary<string,int>
                    {
                        ["攻击"]=p.AttackBonus,["防御"]=p.DefenseBonus,["移动"]=p.MovementBonus,
                        ["先攻"]=p.InitiativeBonus,["范围"]=p.RangeBonus,["远程"]=p.RangedBonus
                    }.Where(b => b.Value!=0).ToDictionary(b => b.Key,b => b.Value),
                    HandCount = p.Cards.Count(c => c.Zone == CardZone.InHand || c.Zone == CardZone.Selected),
                    Revealed = p.Cards.Where(c => c.Zone == CardZone.PlayedUnresolved || c.Zone == CardZone.PlayedResolved).ToList(),
                    DiscardColors = p.Cards.Where(c => c.Zone == CardZone.Discarded).Select(c => catalog.Card(c.CardId).Color).ToList()
                }).ToList(),
                OwnCards = seat.HasValue && seat >= 0 && seat < 4 ? snapshot.Players[seat.Value].Cards : new System.Collections.Generic.List<CardInstance>(),
                AvailableHeroes = snapshot.Phase == Phase.HeroSelection ? catalog.Heroes.Where(h => !snapshot.Players.Any(p => p.Seat != seat && p.HeroId == h.Id)).Select(h => h.Id).ToList() : new System.Collections.Generic.List<string>(),
                Events = snapshot.Events.Where(e => e.PrivateTo == null || e.PrivateTo == seat).ToList()
            };
            int playedRound = 1, playedTurn = 1;
            foreach (var entry in snapshot.Events)
            {
                if (entry.Kind == "PlanningStarted")
                {
                    var parts = entry.Detail.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int round) && int.TryParse(parts[1], out int turn)) { playedRound = round; playedTurn = turn; }
                }
                if (entry.Kind == "CardRevealed" && entry.Seat.HasValue && entry.CardId != null)
                    view.Players[entry.Seat.Value].Plays.Add(new PublicPlay { Round = playedRound, Turn = playedTurn, CardId = entry.CardId, Color = catalog.Card(entry.CardId).Color });
            }
            if (seat.HasValue && seat >= 0 && seat < 4)
            {
                foreach (var player in snapshot.Players)
                {
                    var cells = GameRules.LegalDeployments(catalog, snapshot, seat.Value, player.Seat);
                    if (cells.Count > 0) view.Deployments.Add(player.Seat, cells);
                }
                view.CanPass = snapshot.Phase == Phase.Action && snapshot.ActiveSeat == seat && snapshot.Pending == null && snapshot.Execution == null;
                var playedCard = snapshot.Players[seat.Value].Cards.SingleOrDefault(c => c.Zone == CardZone.PlayedUnresolved);
                view.PrimarySupported = playedCard != null && CombatRules.HasPrimaryProgram(catalog.Card(playedCard.CardId),snapshot.EngineVersion);
                view.PrimaryRestriction = playedCard == null ? "" : EffectRules.SkillRestriction(catalog,snapshot,seat.Value,catalog.Card(playedCard.CardId));
                view.CanBeginPrimary = view.CanPass && view.PrimarySupported && view.PrimaryRestriction == "" && snapshot.Units.Any(u => u.Seat == seat);
                view.AttackTargets = CombatRules.AttackTargets(catalog, snapshot, seat.Value);
                view.DefenseOptions = CombatRules.DefenseOptions(catalog, snapshot, seat.Value);
                view.DefenseRestrictions = CombatRules.DefenseRestrictions(catalog,snapshot,seat.Value);
                view.ForcedDiscardCards = GameRules.LegalForcedDiscards(snapshot,seat.Value);
                view.CanDeclineRetaliationDiscard = GameRules.CanDeclineRetaliationDiscard(catalog,snapshot,seat.Value);
                view.OptionalDiscardCards = GameRules.LegalOptionalDiscards(catalog,snapshot,seat.Value);
                view.RecoverableCards = GameRules.LegalRecoveries(catalog,snapshot,seat.Value);
                view.CardSwapOptions = GameRules.LegalCardSwaps(catalog,snapshot,seat.Value);
                view.MinionProtectionCards = GameRules.LegalMinionProtectionCards(snapshot,seat.Value);
                view.PrimaryOptions = GameRules.LegalPrimaryOptions(catalog,snapshot,seat.Value);
                view.EffectTargets = GameRules.LegalEffectTargets(catalog,snapshot,seat.Value);
                view.GoldTransfers = GameRules.LegalGoldTransfers(catalog,snapshot,seat.Value);
                view.UnimplementedDefenseCards = CombatRules.UnimplementedDefenses(catalog, snapshot, seat.Value);
                view.RespawnCells = GameRules.LegalRespawns(catalog, snapshot, seat.Value);
                view.CanResolveRoundEnd = GameRules.CanResolveRoundEnd(snapshot);
                view.RoundMinionRemovals = GameRules.LegalRoundMinionRemovals(snapshot, seat.Value);
                view.UpgradeOptions = GameRules.LegalUpgrades(catalog, snapshot, seat.Value);
                view.DebugAttackTargets = GameRules.LegalDebugAttacks(snapshot, seat.Value);
                view.OwnUpgradeHistory = snapshot.Players[seat.Value].UpgradeHistory;
                if (snapshot.Pending?.Kind == "minion_spawn" && snapshot.Pending.ChooserSeat == seat && snapshot.Frontline != null)
                    foreach (var spawn in snapshot.Frontline.Remaining.Where(s => snapshot.Pending.CandidateUnits.Count > 0 ? snapshot.Pending.CandidateUnits.Contains(s.Unit.Id) : s.Unit.Id == snapshot.Pending.UnitId))
                        view.SpawnChoices.Add(spawn.Unit.Id, GameRules.LegalMinionSpawns(catalog, snapshot, seat.Value, spawn.Unit.Id));
                view.SecondaryMoves = MovementRules.LegalMoves(catalog, snapshot, seat.Value, MoveMode.Secondary);
                view.FastMoves = MovementRules.LegalMoves(catalog, snapshot, seat.Value, MoveMode.Fast);
                view.EffectMoves = GameRules.LegalEffectMoves(catalog,snapshot,seat.Value);
                view.Placements = GameRules.LegalPlacements(catalog,snapshot,seat.Value);
                view.MinionReturns = GameRules.LegalMinionReturns(catalog,snapshot,seat.Value);
                if (snapshot.Sandbox)
                    foreach (var unit in snapshot.Units) view.DebugTeleports.Add(unit.Id, GameRules.LegalDebugTeleports(catalog, snapshot, unit.Id));
            }
            // Mask private origins only after every legal query has used the complete snapshot.
            if (view.Pending?.SourcePrivateTo != null && view.Pending.SourcePrivateTo != seat) view.Pending.Source="";
            foreach (var effect in view.Effects)
                if (effect.SourcePrivateTo.HasValue && effect.SourcePrivateTo != seat) effect.SourceCardId="";
            return view;
        }
        private static string Fingerprint(string value)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}

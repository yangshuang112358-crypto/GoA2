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
        private static bool IsMinion(UnitState unit) => unit.Kind == "melee" || unit.Kind == "ranged" || unit.Kind == "heavy";
        private static Team OtherTeam(Team team) => team == Team.Blue ? Team.Red : Team.Blue;
        public static List<string> LegalMinionRemovals(GameState state, bool bypassProtection = false)
        {
            if (state.Phase == Phase.Finished) return new List<string>();
            return state.Units.Where(u => IsMinion(u) && (bypassProtection || u.Kind != "heavy" ||
                !state.Units.Any(other => other.Id != u.Id && IsMinion(other) && other.Team == u.Team)))
                .Select(u => u.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }
        public static List<Hex> LegalMinionSpawns(ContentCatalog catalog, GameState state, int seat, string unitId = "")
        {
            if (state.Phase != Phase.EffectChoice || state.Frontline == null || state.Pending?.Kind != "minion_spawn" || state.Pending.ChooserSeat != seat)
                return new List<Hex>();
            if (unitId == "") unitId = state.Pending.UnitId;
            if (state.Pending.CandidateUnits.Count > 0 ? !state.Pending.CandidateUnits.Contains(unitId) : unitId != state.Pending.UnitId)
                return new List<Hex>();
            var spawn = state.Frontline.Remaining.FirstOrDefault(s => s.Unit.Id == unitId);
            return spawn == null ? new List<Hex>() : SpawnCells(catalog, state, spawn);
        }
        private static List<Hex> SpawnCells(ContentCatalog catalog, GameState state, MinionSpawn spawn)
        {
            var occupied = new HashSet<Hex>(state.Units.Select(u => u.Position));
            if (!occupied.Contains(spawn.Origin)) return new List<Hex> { spawn.Origin };
            return spawn.Origin.Neighbors().Where(h => !occupied.Contains(h) && catalog.Cell(h)?.Obstacle == false)
                .OrderBy(h => h.X).ThenBy(h => h.Y).ToList();
        }
        private static void RemoveMinion(ContentCatalog catalog, GameState state, Command command, string unitId,
            string source, int? rewardSeat = null, bool bypassProtection = false, bool finishActionOnResume = false, bool resumeCardExecution = false, bool resumeRoundEnd = false, bool preventionResolved = false)
        {
            Require(state.Frontline == null, "pending_choice", "请先完成当前推进出生。");
            Require(LegalMinionRemovals(state, bypassProtection).Contains(unitId), "invalid_minion", "小兵不存在或重型仍受友军保护。");
            var unit = state.Units.Single(u => u.Id == unitId);
            if (rewardSeat.HasValue)
                Require(rewardSeat >= 0 && rewardSeat < 4 && state.Players[rewardSeat.Value].Team != unit.Team, "invalid_attacker", "击败奖励只能归于敌方玩家。");
            if(!preventionResolved && rewardSeat.HasValue && BeginMinionProtection(catalog,state,command,unit,source,rewardSeat.Value,bypassProtection,finishActionOnResume,resumeCardExecution,resumeRoundEnd))return;
            state.Units.Remove(unit);
            Emit(state, command, rewardSeat.HasValue ? "MinionDefeated" : "MinionRemoved", rewardSeat, detail: unit.Id);
            state.Events.Last().From = unit.Position;
            if (rewardSeat.HasValue)
            {
                int gold = unit.Kind == "heavy" ? 4 : 2;
                state.Players[rewardSeat.Value].Gold += gold;
                Emit(state, command, "GoldAwarded", rewardSeat, detail: gold.ToString(CultureInfo.InvariantCulture));
            }
            if (unit.Kind == "heavy") AdvanceFrontline(catalog, state, command, unit.Team, source, finishActionOnResume, resumeCardExecution, resumeRoundEnd);
            else if (finishActionOnResume) FinishAction(catalog, state, command);
        }
        private static void AdvanceFrontline(ContentCatalog catalog, GameState state, Command command, Team defeated, string source, bool finishActionOnResume, bool resumeCardExecution, bool resumeRoundEnd)
        {
            Require(state.Frontline == null, "nested_frontline", "当前推进尚未完成。");
            var resume = new FrontlineTransition
            {
                ResumePhase = state.Phase, ResumeActiveSeat = state.ActiveSeat, ResumePending = state.Pending,
                Source = source, FinishActionOnResume = finishActionOnResume, ResumeCardExecution = resumeCardExecution, ResumeRoundEnd = resumeRoundEnd
            };
            var winner = OtherTeam(defeated);
            string[] regions = { "blueFountain", "blueNear", "mid", "redNear", "redFountain" };
            int index = Array.IndexOf(regions, state.CombatRegion);
            Require(index > 0 && index < 4, "invalid_combat_region", "当前战区不能继续推进。");
            int cleared = state.Units.RemoveAll(IsMinion);
            Emit(state, command, "MinionsCleared", detail: cleared.ToString(CultureInfo.InvariantCulture));
            state.CombatRegion = regions[index + (winner == Team.Blue ? 1 : -1)];
            state.FrontlineSequence++;
            if (winner == Team.Blue) state.BlueMarks++; else state.RedMarks++;
            Emit(state, command, "FrontlineAdvanced", detail: state.CombatRegion);
            Emit(state, command, "FrontlineMarkGained", detail: winner.ToString());
            if (state.BlueMarks >= state.VictoryMarksRequired || state.RedMarks >= state.VictoryMarksRequired)
            { DeclareVictory(state, command, winner, "frontline_marks"); return; }
            if (state.CombatRegion.EndsWith("Fountain", StringComparison.Ordinal))
            { DeclareVictory(state, command, winner, "fountain"); return; }
            state.Frontline = resume; state.Pending = null; state.ActiveSeat = null; state.Phase = Phase.EffectChoice;
            foreach (var cell in catalog.Cells.Where(c => c.Region == state.CombatRegion && c.Spawn.EndsWith("Spawn", StringComparison.Ordinal) && !c.Spawn.Contains("Hero"))
                .OrderBy(c => c.Position.X).ThenBy(c => c.Position.Y))
            {
                var spawn = new MinionSpawn
                {
                    Origin = cell.Position,
                    Unit = new UnitState
                    {
                        Id = "minion:" + state.FrontlineSequence + ":" + cell.Position, Position = cell.Position,
                        Team = cell.Spawn.StartsWith("blue", StringComparison.Ordinal) ? Team.Blue : Team.Red,
                        Kind = cell.Spawn.Contains("Heavy") ? "heavy" : cell.Spawn.Contains("Ranged") ? "ranged" : "melee"
                    }
                };
                if (state.Units.Any(u => u.Position == cell.Position)) resume.Remaining.Add(spawn);
                else SpawnMinion(state, command, spawn, cell.Position);
            }
            ContinueFrontline(catalog, state, command);
        }
        private static void SpawnMinion(GameState state, Command command, MinionSpawn spawn, Hex position)
        {
            spawn.Unit.Position = position; state.Units.Add(spawn.Unit);
            Emit(state, command, "MinionSpawned", detail: spawn.Unit.Id);
            state.Events.Last().To = position;
        }
        private static void ContinueFrontline(ContentCatalog catalog, GameState state, Command command)
        {
            var progress = state.Frontline!;
            if (progress.Remaining.Count == 0)
            {
                state.Phase = progress.ResumePhase; state.ActiveSeat = progress.ResumeActiveSeat; state.Pending = progress.ResumePending; state.Frontline = null;
                Emit(state, command, "FrontlineCompleted", detail: state.CombatRegion);
                if (progress.FinishActionOnResume) FinishAction(catalog, state, command);
                else if (progress.ResumeCardExecution) ContinueCard(catalog, state, command);
                else if (progress.ResumeRoundEnd) ContinueRoundMinionBattle(catalog, state, command);
                return;
            }
            var next = progress.Remaining[0];
            var units = new List<string>();
            if (state.EngineVersion >= 1)
            {
                var teams = progress.Remaining.Select(s => s.Unit.Team).Distinct().ToList();
                var team = teams.Count > 1 ? state.DecisionCoin : teams[0];
                units = progress.Remaining.Where(s => s.Unit.Team == team).Select(s => s.Unit.Id).ToList();
                next = progress.Remaining.First(s => s.Unit.Id == units[0]);
            }
            bool conflict = state.EngineVersion == 0 && progress.Remaining.Any(a => progress.Remaining.Any(b => a != b && SpawnCells(catalog, state, a).Intersect(SpawnCells(catalog, state, b)).Any()));
            state.Pending = new PendingChoice
            {
                Id = "spawn:" + state.FrontlineSequence + ":" + (state.Events.Count + 1),
                Kind = conflict ? "spawn_order_unresolved" : "minion_spawn", ChooserSeat = Captain(state, next.Unit.Team),
                UnitId = next.Unit.Id, Source = progress.Source, ResumeAt = "frontline_spawns", Optional = false,
                CandidateCells = conflict ? new List<Hex>() : SpawnCells(catalog, state, next), CandidateUnits = units
            };
            Emit(state, command, conflict ? "SpawnOrderRulingRequired" : "MinionSpawnChoiceRequired", state.Pending.ChooserSeat, detail: next.Unit.Id);
        }
        private static void ChooseMinionSpawn(ContentCatalog catalog, GameState state, Command command)
        {
            Require(LegalMinionSpawns(catalog, state, command.ActorSeat, command.Value).Contains(command.Destination), "invalid_spawn", "请由指定队长选择合法出生空格。");
            string unitId = command.Value == "" ? state.Pending!.UnitId : command.Value;
            var spawn = state.Frontline!.Remaining.Single(s => s.Unit.Id == unitId);
            SpawnMinion(state, command, spawn, command.Destination); state.Frontline.Remaining.Remove(spawn);
            ContinueFrontline(catalog, state, command);
        }
        private static void DeclareVictory(GameState state, Command command, Team winner, string reason)
        {
            state.Winner = winner; state.VictoryReason = reason; state.Phase = Phase.Finished;
            state.ActiveSeat = null; state.Pending = null; state.Frontline = null; state.Execution = null; state.RoundEnd = null; state.MinionDefeat = null; state.BeforeAction = null;
            Emit(state, command, "MatchWon", detail: winner + ":" + reason);
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Goa2.Application;
using Goa2.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Goa2.Infrastructure.Scenarios
{
    public sealed class ScenarioRunner
    {
        private readonly ContentCatalog catalog;
        private readonly ScenarioDefinition definition;
        private readonly JsonStateCodec codec = new JsonStateCodec();
        public GameSession Session { get; private set; }
        public ScenarioReport Report { get; } = new ScenarioReport();
        public bool Complete => Report.Complete;
        public ScenarioRunner(ContentCatalog catalog, ScenarioDefinition definition)
        {
            this.catalog = ContentSnapshot.Copy(catalog);
            this.definition = Load(JsonConvert.SerializeObject(definition, Settings()));
            Session = LocalGameFactory.Create(this.catalog, "scenario:" + definition.Id, definition.Players, definition.Seed, definition.Sandbox);
            Report.Id = definition.Id; Report.Name = definition.Name; Report.Seed = definition.Seed; Report.TotalSteps = definition.Steps.Count;
            Report.ContentHash = catalog.Hash; Report.RulesVersion = catalog.Rules.Version; Report.ProtocolVersion = GameState.CurrentProtocol;
            Report.FinalStateHash = Hash(Session.ExportSave());
        }
        private static JsonSerializerSettings Settings() => new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error,
            DateParseHandling = DateParseHandling.None, MaxDepth = 64
        };
        private static void Guard(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        public static ScenarioDefinition Load(string json)
        {
            try
            {
                Guard(json != null && Encoding.UTF8.GetByteCount(json) <= 2*1024*1024, "场景输入为空或超过2 MiB。");
                var token = JsonStateCodec.ParseStrict(json!);
                Shape(token,typeof(ScenarioDefinition),"scenario");
                var definition = token.ToObject<ScenarioDefinition>(JsonSerializer.Create(Settings()));
                Guard(definition != null, "场景必须为对象。"); Validate(definition!); return definition!;
            }
            catch (JsonException error) { throw new InvalidDataException("场景JSON无效：" + error.Message, error); }
        }
        private static void Shape(JToken token, Type type, string path)
        {
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) { if (token.Type == JTokenType.Null) return; type = nullable; }
            if (type == typeof(string)) { Guard(token.Type == JTokenType.String || token.Type == JTokenType.Null,path+"须为字符串。"); return; }
            if (type == typeof(bool)) { Guard(token.Type == JTokenType.Boolean,path+"须为布尔值。"); return; }
            if (type == typeof(int))
            {
                Guard(token.Type == JTokenType.Integer && int.TryParse(token.ToString(),System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out _),path+"须为32位整数。"); return;
            }
            if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                Guard(token is JArray,path+"须为数组。");
                Type elementType = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
                foreach (var element in (JArray)token) Shape(element,elementType,path+"[]");
                return;
            }
            Guard(token is JObject,path+"须为对象。"); var obj = (JObject)token;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                foreach (var property in obj.Properties()) Shape(property.Value,type.GetGenericArguments()[1],path+"."+property.Name);
                return;
            }
            var fields = type.GetFields(BindingFlags.Public|BindingFlags.Instance).ToDictionary(f => f.Name,StringComparer.Ordinal);
            foreach (var property in obj.Properties())
            {
                Guard(fields.TryGetValue(property.Name,out var field),path+"包含未知字段："+property.Name);
                Shape(property.Value,field!.FieldType,path+"."+property.Name);
            }
            if (type == typeof(Hex)) Guard(obj.Property("X") != null && obj.Property("Y") != null,path+"需要同时给出X和Y。");
        }
        private static void Validate(ScenarioDefinition definition)
        {
            Guard(definition.SchemaVersion == 1, "场景SchemaVersion必须为1。");
            Guard(definition.Id != null && Regex.IsMatch(definition.Id, "\\A[a-z0-9-]{1,64}\\z"), "场景Id格式无效。");
            Guard(!string.IsNullOrWhiteSpace(definition.Name) && definition.Name.Length <= 200, "请提供有效场景名称。");
            Guard(definition.Players != null && definition.Players.Length == 4 && definition.Players.All(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 40), "场景需要四名玩家。");
            Guard(definition.Steps != null && definition.Steps.Count > 0 && definition.Steps.Count <= 1000, "场景需要1至1000步。");
            for (int index = 0; index < definition.Steps!.Count; index++)
            {
                var step = definition.Steps[index]; Guard(step != null && step.Expect != null, "步骤和Expect不能为空。");
                Guard(step!.Name != null && step.Value != null && step.Value.Length <= 4096, "步骤文字或参数无效。");
                ValidateSeat(step.Actor, false); ValidateSeat(step.Target, true);
                if (step.AuthenticateAs != null) ValidateSeat(step.AuthenticateAs, false);
                Guard(Enum.TryParse<MoveMode>(step.MoveMode, out var moveMode) && moveMode.ToString() == step.MoveMode, "MoveMode无效。");
                if (step.ReplayStep.HasValue)
                {
                    Guard(step.ReplayStep >= 1 && step.ReplayStep <= index, "ReplayStep必须指向更早的步骤。");
                    Guard(step.Command == "" && step.Value == "" && step.Target == "none" && step.Destination == null && step.Actor == "p1" && step.MoveMode == "Secondary", "重复步骤不能同时定义新的命令字段。");
                }
                else Guard(Enum.TryParse<CommandKind>(step.Command, out var kind) && Enum.IsDefined(typeof(CommandKind), kind) && kind.ToString() == step.Command, "步骤Command不存在。");
                var expect = step.Expect!;
                Guard(!string.IsNullOrWhiteSpace(expect.Code), "Expect.Code不能为空。");
                if (expect.Phase != null) Guard(Enum.TryParse<Phase>(expect.Phase, out var phase) && Enum.IsDefined(typeof(Phase), phase) && phase.ToString() == expect.Phase, "Expect.Phase不存在。");
                if (expect.Active != null) ValidateSeat(expect.Active, true);
                if (expect.Winner != null) Guard(expect.Winner == "none" || expect.Winner == "Blue" || expect.Winner == "Red", "Expect.Winner须为Blue、Red或none。");
                if (expect.PendingChooser != null) ValidateSeat(expect.PendingChooser, true);
                ValidateCounts(expect.Gold); ValidateCounts(expect.HandCounts); ValidateCounts(expect.DiscardCounts);
                ValidateCounts(expect.Levels); ValidateCounts(expect.UpgradeCounts);
                Guard(expect.Levels.All(p => p.Value >= 1 && p.Value <= 8) && expect.UpgradeCounts.All(p => p.Value <= 6), "升级期望超出等级1—8或选择0—6的范围。");
                Guard(expect.PurpleCards != null && expect.PurpleCards.All(p => Regex.IsMatch(p.Key,"\\Ap[1-4]\\z") && !string.IsNullOrWhiteSpace(p.Value)), "紫卡期望须使用p1至p4及卡牌ID或none。");
                if (expect.RoundEndStage != null) Guard(expect.RoundEndStage == "none" || expect.RoundEndStage == "minion_battle" || expect.RoundEndStage == "upgrades", "轮末阶段期望无效。");
                if (expect.UpgradingPlayers.HasValue) Guard(expect.UpgradingPlayers >= 0 && expect.UpgradingPlayers <= 4, "待升级人数须为0—4。");
                if (expect.RemainingMinionRemovals.HasValue) Guard(expect.RemainingMinionRemovals >= 0, "待移除小兵数不能为负。");
                Guard(expect.EffectCounts != null && expect.EffectCounts.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value>=0), "持续效果期望须为来源卡牌ID及非负数量。");
                Guard(expect.PrimaryRestrictions != null && expect.PrimaryRestrictions.All(p => Regex.IsMatch(p.Key,"\\Ap[1-4]\\z") && !string.IsNullOrWhiteSpace(p.Value)), "行动禁止期望须使用p1至p4及来源卡牌ID或none。");
                Guard(expect.Positions != null && expect.EventCounts != null && expect.EventOrder != null, "期望集合不能为null。");
                Guard(expect.EventCounts!.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value >= 0) && expect.EventOrder!.All(e => !string.IsNullOrWhiteSpace(e)), "期望事件无效。");
            }
        }
        private static void ValidateCounts(Dictionary<string,int> counts)
        {
            Guard(counts != null && counts.All(p => Regex.IsMatch(p.Key, "\\Ap[1-4]\\z") && p.Value >= 0), "席位数量期望须使用p1至p4和非负数。");
        }
        private static void ValidateSeat(string value, bool allowNone)
        {
            Guard(value != null && ((allowNone && value == "none") || value == "active" || value == "chooser" || value == "blueCaptain" || value == "redCaptain" ||
                (Regex.IsMatch(value, "\\Ap[0-9]+\\z") && int.TryParse(value.Substring(1), out _))), "席位引用无效。");
        }
        private static int Seat(string value, GameView view)
        {
            switch (value)
            {
                case "none": return -1;
                case "active": return view.ActiveSeat ?? throw new InvalidDataException("当前没有行动席位。");
                case "chooser": return view.Pending?.ChooserSeat ?? throw new InvalidDataException("当前没有待选择席位。");
                case "blueCaptain": return view.BlueCaptain;
                case "redCaptain": return view.RedCaptain;
                default: return int.Parse(value.Substring(1), System.Globalization.CultureInfo.InvariantCulture)-1;
            }
        }
        private Command Clone(Command command) => JsonConvert.DeserializeObject<Command>(codec.WriteCommand(command), Settings())!;
        private Command BuildCommand(ScenarioStep step, GameView view)
        {
            if (step.ReplayStep.HasValue)
                return Clone(Report.Steps[step.ReplayStep.Value-1].Command ?? throw new InvalidDataException("原步骤没有有效命令。"));
            return new Command
            {
                Id = definition.Id + ":" + (Report.Steps.Count+1), MatchId = view.MatchId, ExpectedRevision = view.Revision,
                ActorSeat = Seat(step.Actor, view), Kind = (CommandKind)Enum.Parse(typeof(CommandKind), step.Command), Value = step.Value,
                TargetSeat = Seat(step.Target, view), Destination = step.Destination ?? default,
                MoveMode = (MoveMode)Enum.Parse(typeof(MoveMode), step.MoveMode)
            };
        }
        public ScenarioStepResult Next()
        {
            if (Complete) throw new InvalidOperationException("场景已结束。");
            var step = definition.Steps[Report.Steps.Count];
            var result = new ScenarioStepResult { Number = Report.Steps.Count+1, Name = string.IsNullOrWhiteSpace(step.Name) ? step.Command : step.Name };
            string before = Session.ExportSave();
            var beforeState = codec.Read(before);
            try
            {
                var beforeView = Session.View(null);
                var command = BuildCommand(step, beforeView); result.Command = Clone(command);
                int authenticated = step.AuthenticateAs == null ? command.ActorSeat : Seat(step.AuthenticateAs, beforeView);
                var outcome = Session.Execute(authenticated, command);
                result.Accepted = outcome.Accepted; result.Duplicate = outcome.Duplicate; result.Code = outcome.Code;
                Equal(result, "Code", step.Expect.Code, outcome.Code);
                Equal(result, "Accepted", step.Expect.Code == "ok" || step.Expect.Code == "duplicate", outcome.Accepted);
                Equal(result, "Duplicate", step.Expect.Code == "duplicate", outcome.Duplicate);
                string after = Session.ExportSave();
                if (!outcome.Accepted && before != after) result.Errors.Add("被拒绝的命令修改了权威状态。");
                Check(step.Expect, result, codec.Read(after), Session.View(null), beforeState.Events.Count);
                if (definition.VerifyReplayAfterEachStep)
                {
                    var restored = LocalGameFactory.Restore(catalog, after);
                    if (restored.ExportSave() != after) result.Errors.Add("存档恢复结果与当前状态不同。");
                    else { Session = restored; result.Restored = true; }
                }
            }
            catch (Exception error) { result.Errors.Add(error.GetType().Name + ": " + error.Message); }
            var final = Session.View(null);
            result.StateHash = Hash(Session.ExportSave()); result.Revision = final.Revision; result.Phase = final.Phase.ToString(); result.Round = final.Round; result.Turn = final.Turn;
            result.Passed = result.Errors.Count == 0; Report.Steps.Add(result);
            Report.FinalStateHash = result.StateHash;
            Report.Complete = !result.Passed || Report.Steps.Count == definition.Steps.Count;
            Report.Passed = Report.Complete && Report.Steps.Count == definition.Steps.Count && Report.Steps.All(s => s.Passed);
            return result;
        }
        private static void Equal<T>(ScenarioStepResult result, string field, T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) result.Errors.Add(field + ": expected " + expected + ", actual " + actual);
        }
        private void Check(ScenarioExpectation expect, ScenarioStepResult result, GameState state, GameView view, int eventStart)
        {
            if (expect.Phase != null) Equal(result, "Phase", expect.Phase, state.Phase.ToString());
            if (expect.Round.HasValue) Equal(result, "Round", expect.Round.Value, state.Round);
            if (expect.Turn.HasValue) Equal(result, "Turn", expect.Turn.Value, state.Turn);
            if (expect.Active != null) Equal(result, "Active", Seat(expect.Active, view), state.ActiveSeat ?? -1);
            if (expect.PendingKind != null) Equal(result, "PendingKind", expect.PendingKind, state.Pending?.Kind ?? "none");
            if (expect.PendingChooser != null) Equal(result, "PendingChooser", Seat(expect.PendingChooser, view), state.Pending?.ChooserSeat ?? -1);
            if (expect.Revealed.HasValue) Equal(result, "Revealed", expect.Revealed.Value, state.Events.Count(e => e.Kind == "CardRevealed"));
            if (expect.AttackBase.HasValue) Equal(result,"AttackBase",expect.AttackBase,state.Execution?.Attack?.BaseAttack);
            if (expect.AttackBonus.HasValue) Equal(result,"AttackBonus",expect.AttackBonus,state.Execution?.Attack?.AttackBonus);
            if (expect.CardTextBonus.HasValue) Equal(result,"CardTextBonus",expect.CardTextBonus,state.Execution?.Attack?.CardTextBonus);
            if (expect.AttackFinal.HasValue) Equal(result,"AttackFinal",expect.AttackFinal,state.Execution?.Attack?.FinalAttack);
            if (expect.CombatRegion != null) Equal(result, "CombatRegion", expect.CombatRegion, state.CombatRegion);
            if (expect.Winner != null) Equal(result, "Winner", expect.Winner, state.Winner?.ToString() ?? "none");
            if (expect.BlueMarks.HasValue) Equal(result, "BlueMarks", expect.BlueMarks.Value, state.BlueMarks);
            if (expect.RedMarks.HasValue) Equal(result, "RedMarks", expect.RedMarks.Value, state.RedMarks);
            if (expect.BlueMinions.HasValue) Equal(result, "BlueMinions", expect.BlueMinions.Value, state.Units.Count(u => u.Kind != "hero" && u.Team == Team.Blue));
            if (expect.RedMinions.HasValue) Equal(result, "RedMinions", expect.RedMinions.Value, state.Units.Count(u => u.Kind != "hero" && u.Team == Team.Red));
            if (expect.BlueCrystal.HasValue) Equal(result, "BlueCrystal", expect.BlueCrystal.Value, state.BlueCrystal);
            if (expect.RedCrystal.HasValue) Equal(result, "RedCrystal", expect.RedCrystal.Value, state.RedCrystal);
            foreach (var pair in expect.Gold) Equal(result, "Gold."+pair.Key, pair.Value, view.Players[Seat(pair.Key,view)].Gold);
            foreach (var pair in expect.HandCounts) Equal(result, "HandCounts."+pair.Key, pair.Value, view.Players[Seat(pair.Key,view)].HandCount);
            foreach (var pair in expect.DiscardCounts) Equal(result, "DiscardCounts."+pair.Key, pair.Value, view.Players[Seat(pair.Key,view)].DiscardColors.Count);
            foreach (var pair in expect.Levels) Equal(result, "Levels."+pair.Key, pair.Value, state.Players[Seat(pair.Key,view)].Level);
            foreach (var pair in expect.UpgradeCounts) Equal(result, "UpgradeCounts."+pair.Key, pair.Value, state.Players[Seat(pair.Key,view)].UpgradeHistory.Count);
            foreach (var pair in expect.PurpleCards) Equal(result, "PurpleCards."+pair.Key, pair.Value, state.Players[Seat(pair.Key,view)].PurpleCardId ?? "none");
            if (expect.RoundEndStage != null) Equal(result,"RoundEndStage",expect.RoundEndStage,state.RoundEnd?.Stage ?? "none");
            if (expect.UpgradingPlayers.HasValue) Equal(result,"UpgradingPlayers",expect.UpgradingPlayers.Value,view.UpgradingSeats.Count);
            if (expect.RemainingMinionRemovals.HasValue) Equal(result,"RemainingMinionRemovals",expect.RemainingMinionRemovals.Value,state.RoundEnd?.RemainingRemovals ?? state.Execution?.Completion?.ActionBattle?.RemainingRemovals ?? 0);
            foreach (var pair in expect.EffectCounts) Equal(result,"EffectCounts."+pair.Key,pair.Value,state.Effects.Count(e => e.SourceCardId==pair.Key));
            foreach (var pair in expect.PrimaryRestrictions)
            {
                string restriction=Session.View(Seat(pair.Key,view)).PrimaryRestriction;
                Equal(result,"PrimaryRestrictions."+pair.Key,pair.Value,restriction=="" ? "none" : restriction);
            }
            foreach (var pair in expect.Positions)
            {
                var unit = state.Units.FirstOrDefault(u => u.Id == pair.Key);
                if (unit == null) result.Errors.Add("Positions."+pair.Key+": unit not found");
                else Equal(result, "Positions."+pair.Key, pair.Value, unit.Position);
            }
            var events = state.Events.Skip(eventStart).ToList();
            foreach (var pair in expect.EventCounts) Equal(result, "EventCounts."+pair.Key, pair.Value, events.Count(e => e.Kind == pair.Key));
            int cursor = 0;
            foreach (string kind in expect.EventOrder)
            {
                int next = events.FindIndex(cursor, e => e.Kind == kind);
                if (next < 0) { result.Errors.Add("EventOrder: missing " + kind + " after event " + cursor); break; }
                cursor = next+1;
            }
        }
        private static string Hash(string value)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}

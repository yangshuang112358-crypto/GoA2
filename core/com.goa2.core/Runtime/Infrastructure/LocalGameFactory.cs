#nullable enable
using System;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Rules;

namespace Goa2.Infrastructure
{
    public static class LocalGameFactory
    {
        public static GameSession Create(ContentCatalog catalog, string matchId, string[] names, int seed, bool sandbox = false, int engineVersion = GameState.CurrentEngineVersion)
            => new GameSession(catalog,new JsonStateCodec(),InitialState(catalog,matchId,names,seed,sandbox,engineVersion));
        private static GameState InitialState(ContentCatalog catalog,string matchId,string[] names,int seed,bool sandbox,int engineVersion)
        {
            var initial = new GameRules().Create(catalog, matchId, names, seed, engineVersion);
            initial.Sandbox = sandbox; initial.QuickSelection = sandbox;
            return initial;
        }
        public static GameSession Restore(ContentCatalog catalog, string json)
        {
            try
            {
                var codec = new JsonStateCodec();
                var saved = codec.Read(json);
                // Validate version before replay, then derive authority from accepted intents.
                _ = new GameSession(catalog, codec, saved);
                if (saved.Players == null || saved.Players.Count != 4 || saved.AcceptedCommands == null || saved.AcceptedCommands.Count > 10000)
                    throw new RuleViolation("invalid_save", "存档结构或命令数量无效。");
                var initial=InitialState(catalog,saved.MatchId,saved.Players.OrderBy(p=>p.Seat).Select(p=>p.Name).ToArray(),saved.Seed,saved.Sandbox,saved.InitialEngineVersion);
                var replay=GameSession.Replay(catalog,codec,initial,saved.AcceptedCommands);
                if (replay.ExportSave() != codec.Write(saved)) throw new RuleViolation("invalid_save", "存档状态与命令重放结果不一致。");
                return replay;
            }
            catch (RuleViolation) { throw; }
            catch (Exception error) when (error is Newtonsoft.Json.JsonException || error is System.IO.InvalidDataException ||
                                          error is ArgumentException || error is InvalidOperationException || error is NullReferenceException)
            { throw new RuleViolation("invalid_save", "无法解析有效对局存档：" + error.Message); }
        }
    }
}

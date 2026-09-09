#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Domain;
using Newtonsoft.Json.Linq;

namespace Goa2.Infrastructure
{
    public static class ContentLoader
    {
        private static readonly string[] Names = { "cards", "heroes", "map", "ruleset" };
        public static ContentCatalog LoadDirectory(string root) => Load(path => File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))));
        public static ContentCatalog Load(Func<string, byte[]> read)
        {
            byte[] manifestBytes = read("content/manifest.json");
            var manifest = JsonStateCodec.ParseStrict(Text(manifestBytes));
            Check((string?)manifest["schema_version"] == "1.0.0", "不支持的内容结构版本。");
            var entries = (manifest["files"] as JArray) ?? throw new InvalidDataException("内容清单缺失。");
            var expectedPaths = Names.Select(n => "content/canonical/" + n + ".json").ToArray();
            var paths = entries.Select(e => (string?)e["path"]).ToArray();
            Check(paths.Length == 4 && paths.Distinct().Count() == 4 && paths.All(p => expectedPaths.Contains(p)), "清单只允许四份正式内容文件。");
            var documents = new Dictionary<string, JToken>();
            foreach (var entry in entries)
            {
                string path = (string)entry["path"]!;
                var bytes = read(path);
                Check(Sha256(bytes) == (string?)entry["sha256"], "内容哈希不匹配：" + path);
                var document = JsonStateCodec.ParseStrict(Text(bytes));
                Check((string?)document["schema_version"] == "1.0.0", "不支持的文件结构：" + path);
                documents.Add(Path.GetFileNameWithoutExtension(path), document);
            }
            var catalog = new ContentCatalog { Version = (string)manifest["content_version"]!, Hash = Sha256(manifestBytes) };
            foreach (var hero in documents["heroes"]["heroes"]!)
                catalog.Heroes.Add(new HeroDefinition { Id = (string)hero["hero_id"]!, Name = (string)hero["name"]! });
            foreach (var card in documents["cards"]["cards"]!)
            {
                var primary = card["primary_action"]!;
                var movement = card["secondary_actions"]!["movement"]!;
                var defense = card["secondary_actions"]!["defense"]!;
                var subtype = primary["subtype"];
                catalog.Cards.Add(new CardDefinition
                {
                    Id = (string)card["id"]!, Name = (string)card["name"]!, HeroId = (string)card["hero_id"]!,
                    Color = (string)card["color_key"]!, Level = (int?)card["level"], Initiative = (int)card["initiative"]!,
                    PrimaryCategory = (string)primary["category"]!, PrimaryFamily = (string)primary["family"]!,
                    Text = (string)primary["text"]!, PrimaryValue = (int)primary["value"]!, Exclamation = (bool)primary["exclamation"]!,
                    Subtype = subtype?.Type == JTokenType.Object ? (string?)subtype["type"] : null,
                    SubtypeValue = subtype?.Type == JTokenType.Object ? (int?)subtype["value"] : null,
                    SecondaryMovement = (bool)movement["has_action"]! ? (int?)movement["value"] : null,
                    SecondaryDefense = (bool)defense["has_action"]! ? (int?)defense["value"] : null,
                    Passive = (string?)card["passive_bonus"]!["type"]
                });
            }
            foreach (var cell in documents["map"]["cells"]!)
                catalog.Cells.Add(new CellDefinition
                {
                    Position = new Hex((int)cell["x"]!, (int)cell["y"]!), Region = (string)cell["region"]!,
                    Obstacle = (bool)cell["obstacle"]!, Lane = (bool)cell["lane"]!, Base = (string?)cell["base"], Spawn = (string)cell["state"]!
                });
            var rules = documents["ruleset"];
            catalog.Rules = new RuleSettings
            {
                Version = (string)rules["rules_version"]!, StartingCrystalLife = (int)rules["starting_crystal_life"]!,
                FrontlineVictoryMarks = (int)rules["frontline_victory_marks"]!, TurnsPerRound = (int)rules["turns_per_round"]!,
                HandSize = (int)rules["hand_size"]!, InitialCombatRegion = (string)rules["initial_combat_region"]!
            };
            Check(catalog.Rules.Version == (string?)manifest["rules_version"], "规则版本与清单不一致。");
            Check(catalog.Heroes.Count == 6 && catalog.Heroes.Select(h => h.Id).Distinct().Count() == 6, "英雄目录不完整。");
            Check(catalog.Cards.Count == 108 && catalog.Cards.Select(c => c.Id).Distinct().Count() == 108, "卡牌目录不完整。");
            Check(catalog.Cells.Count == 254 && catalog.Cells.Select(c => c.Position).Distinct().Count() == 254 && catalog.Cells.Count(c => c.Obstacle) == 44, "地图目录不完整。");
            Check(catalog.Cards.All(c => catalog.Heroes.Any(h => h.Id == c.HeroId) && c.Color != "" && (c.SecondaryMovement == null || c.SecondaryMovement > 0)), "卡牌引用或次要行动无效。");
            return catalog;
        }
        private static string Text(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);
        private static void Check(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        private static string Sha256(byte[] bytes)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}

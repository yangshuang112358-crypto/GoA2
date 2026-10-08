using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Domain;
using Goa2.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Goa2.Ai
{
    public sealed class PublicRuleProfile
    {
        public string Id="", InitialCombatRegion="";
        public int StartingCrystalLife, VictoryMarksRequired, TurnsPerRound, HandSize;
        public static PublicRuleProfile From(ContentCatalog c) => new PublicRuleProfile {
            Id="victory-v1-life-"+c.Rules.StartingCrystalLife+"-marks-"+c.Rules.FrontlineVictoryMarks,
            StartingCrystalLife=c.Rules.StartingCrystalLife, VictoryMarksRequired=c.Rules.FrontlineVictoryMarks,
            InitialCombatRegion=c.Rules.InitialCombatRegion, TurnsPerRound=c.Rules.TurnsPerRound, HandSize=c.Rules.HandSize };
        public PublicRuleProfile CopyFor(int marks)
        {
            if(marks!=VictoryMarksRequired) throw new InvalidOperationException("rule_profile_state_mismatch");
            return new PublicRuleProfile { Id=Id, InitialCombatRegion=InitialCombatRegion, StartingCrystalLife=StartingCrystalLife, VictoryMarksRequired=marks, TurnsPerRound=TurnsPerRound, HandSize=HandSize };
        }
    }
    public static class ProfileCatalog
    {
        // Create a complete, hash-checked alternate content bundle. Never change the user's canonical content.
        public static ContentCatalog Load(string root, int? life=null, int? marks=null, string? exportRoot=null)
        {
            if(life<=0 || marks<=0) throw new ArgumentOutOfRangeException("Victory parameters must be positive.");
            var paths=new[]{"content/manifest.json","content/canonical/cards.json","content/canonical/heroes.json","content/canonical/map.json","content/canonical/ruleset.json"};
            var bytes=paths.ToDictionary(p=>p,p=>File.ReadAllBytes(Path.Combine(root,p)));
            // Check original content before deriving a profile.
            var original=ContentLoader.Load(p=>bytes[p]);
            if((life.HasValue && life!=original.Rules.StartingCrystalLife) || (marks.HasValue && marks!=original.Rules.FrontlineVictoryMarks))
            {
                var rules=JObject.Parse(Encoding.UTF8.GetString(bytes[paths[4]]));
                rules["starting_crystal_life"]=life??original.Rules.StartingCrystalLife;
                rules["frontline_victory_marks"]=marks??original.Rules.FrontlineVictoryMarks;
                bytes[paths[4]]=Encoding.UTF8.GetBytes(rules.ToString(Formatting.Indented));
                var manifest=JObject.Parse(Encoding.UTF8.GetString(bytes[paths[0]]));
                foreach(var entry in manifest["files"]!) if((string?)entry["path"]==paths[4])
                    entry["sha256"]=Convert.ToHexString(SHA256.HashData(bytes[paths[4]])).ToLowerInvariant();
                bytes[paths[0]]=Encoding.UTF8.GetBytes(manifest.ToString(Formatting.Indented));
            }
            var result=ContentLoader.Load(p=>bytes[p]);
            if(exportRoot!=null) foreach(var pair in bytes)
            {
                string path=Path.Combine(exportRoot,pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file=new FileStream(path,FileMode.CreateNew); file.Write(pair.Value);
            }
            return result;
        }
    }
}

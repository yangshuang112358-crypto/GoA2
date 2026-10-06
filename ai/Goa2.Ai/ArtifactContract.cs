using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Goa2.Domain;
using Goa2.Rules;
using Goa2.Application;

namespace Goa2.Ai
{
    public sealed class ArtifactContract
    {
        public int ObservationVersion=Observation.Format, ActionVersion=Candidate.Format, EngineVersion=GameState.CurrentEngineVersion;
        public string ContentHash="", RulesVersion="", ContentVersion="";
        public Dictionary<string,string> AuthorityHashes=new Dictionary<string,string>();
        public static ArtifactContract Current(ContentCatalog catalog) => new ArtifactContract
        {
            ContentHash=catalog.Hash, ContentVersion=catalog.Version, RulesVersion=catalog.Rules.Version,
            AuthorityHashes=new[]{typeof(GameState).Assembly,typeof(GameRules).Assembly,typeof(GameSession).Assembly}
                .ToDictionary(a=>a.GetName().Name!,a=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))).ToLowerInvariant())
        };
        public void RequireCompatible(ArtifactContract expected)
        {
            if(ObservationVersion!=expected.ObservationVersion || ActionVersion!=expected.ActionVersion || EngineVersion!=expected.EngineVersion ||
                ContentHash!=expected.ContentHash || RulesVersion!=expected.RulesVersion || ContentVersion!=expected.ContentVersion ||
                AuthorityHashes.Count!=expected.AuthorityHashes.Count || AuthorityHashes.Any(p=>!expected.AuthorityHashes.TryGetValue(p.Key,out var hash) || hash!=p.Value))
                throw new InvalidDataException("incompatible_ai_artifact: explicit migration and fresh evaluation required");
        }
    }
}

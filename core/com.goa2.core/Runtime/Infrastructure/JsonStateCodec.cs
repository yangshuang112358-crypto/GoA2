#nullable enable
using System;
using System.IO;
using System.Reflection;
using Goa2.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Goa2.Infrastructure
{
    public sealed class JsonStateCodec : IStateCodec
    {
        private sealed class StateContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member,MemberSerialization serialization)
            {
                var property=base.CreateProperty(member,serialization);
                // New execution facts must not add default fields to historical captures.
                if(member.DeclaringType==typeof(CardExecution) && (member.Name==nameof(CardExecution.ActionInstanceId) || member.Name==nameof(CardExecution.FromDiscard) || member.Name==nameof(CardExecution.PreAttackMoved) || member.Name==nameof(CardExecution.AttackTargetCell) || member.Name==nameof(CardExecution.DisplacedMinions) || member.Name==nameof(CardExecution.ReturningMinionId) || member.Name==nameof(CardExecution.RemainingUnitTargets) || member.Name==nameof(CardExecution.AffectedHeroTargets) || member.Name==nameof(CardExecution.ReturnSourceAtEnd) || member.Name==nameof(CardExecution.ActionStopped) || member.Name==nameof(CardExecution.Completion) || member.Name==nameof(CardExecution.ForcedPayment) || member.Name==nameof(CardExecution.UltimateRepeatUsed) || member.Name==nameof(CardExecution.UltimateRepeatExcludedTarget)))
                    property.DefaultValueHandling=DefaultValueHandling.Ignore;
                if(member.DeclaringType==typeof(ActiveEffect) && member.Name==nameof(ActiveEffect.ExemptControllerSeat))
                    property.DefaultValueHandling=DefaultValueHandling.Ignore;
                if(member.DeclaringType==typeof(GameState) && (member.Name==nameof(GameState.MinionDefeat) || member.Name==nameof(GameState.BeforeAction) || member.Name==nameof(GameState.DiscardReactions) || member.Name==nameof(GameState.DiscardReactionFrames)))
                    property.DefaultValueHandling=DefaultValueHandling.Ignore;
                if(member.DeclaringType==typeof(AttackBreakdown) && member.Name==nameof(AttackBreakdown.UltimateBonus))
                    property.DefaultValueHandling=DefaultValueHandling.Ignore;
                if(member.DeclaringType==typeof(RoundEndProgress) && member.Name==nameof(RoundEndProgress.HeroContributions))
                    property.DefaultValueHandling=DefaultValueHandling.Ignore;
                if(member.DeclaringType==typeof(ActionCompletionProgress) && member.Name==nameof(ActionCompletionProgress.ActionBattle))
                    property.DefaultValueHandling=DefaultValueHandling.Ignore;
                if(member.DeclaringType==typeof(FrontlineTransition) && member.Name==nameof(FrontlineTransition.ResumeActionMinionBattle))
                    property.DefaultValueHandling=DefaultValueHandling.Ignore;
                return property;
            }
        }
        private static readonly IContractResolver Contract=new StateContractResolver();
        private static JsonSerializerSettings Settings() => new JsonSerializerSettings
        {
            ContractResolver=Contract,
            TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error,
            MaxDepth = 64, DateParseHandling = DateParseHandling.None, Formatting = Formatting.None
        };
        public string Write(GameState state) => JsonConvert.SerializeObject(state, Settings());
        public string WriteCommand(Command command) => JsonConvert.SerializeObject(command, Settings());
        public GameState Read(string json)
        {
            if (json == null || json.Length > 32 * 1024 * 1024) throw new InvalidDataException("存档为空或超过32 MiB。");
            var token = ParseStrict(json);
            return token.ToObject<GameState>(JsonSerializer.Create(Settings())) ?? throw new InvalidDataException("存档不是对局状态。");
        }
        public static JToken ParseStrict(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
            var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
            if (reader.Read()) throw new InvalidDataException("JSON后存在多余内容。");
            return token;
        }
    }
}

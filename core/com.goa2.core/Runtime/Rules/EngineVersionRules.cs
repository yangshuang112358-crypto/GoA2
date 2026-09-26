#nullable enable
using System.Globalization;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public sealed partial class GameRules
    {
        public static bool CanUpgradeEngine(GameState state) => state.EngineVersion < GameState.CurrentEngineVersion &&
            state.Phase == Phase.Planning && state.Pending == null && state.Frontline == null && state.Execution == null && state.RoundEnd == null && state.Effects.Count == 0 && state.BeforeAction == null && state.DiscardReactions == null && state.DiscardReactionFrames == null &&
            !state.Players.SelectMany(p => p.Cards).Any(c => c.Zone == CardZone.Selected || c.Zone == CardZone.PlayedUnresolved);
        private static void UpgradeEngine(GameState state, Command command)
        {
            Require(CanUpgradeEngine(state) && int.TryParse(command.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int next) &&
                next > state.EngineVersion && next <= GameState.CurrentEngineVersion, "invalid_engine_upgrade", "只能在尚未选牌、没有待处理行动的暗选阶段采用更新的受支持规则。");
            int previous = state.EngineVersion;
            state.EngineVersion = int.Parse(command.Value, CultureInfo.InvariantCulture);
            Emit(state, command, "EngineUpgraded", detail: previous + ":" + state.EngineVersion);
        }
    }
}

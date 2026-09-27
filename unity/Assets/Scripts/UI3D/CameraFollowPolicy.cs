#nullable enable
using System.Linq;
using Goa2.Domain;
namespace Goa2.Presentation.UI3D
{
    // View-only policy. Never selects a seat or computes legal game actions.
    public static class CameraFollowPolicy
    {
        public static Hex? Target(GameView view, int ownSeat)
        {
            string kind=view.Pending?.Kind ?? "";
            if(kind=="round_minion_removal" || kind=="action_minion_removal" ||
                kind=="minion_spawn" || kind=="minion_return" || kind=="spawn_order_unresolved") return null;
            bool own=view.Phase==Phase.Planning || view.RoundEndStage=="upgrades" || view.Phase==Phase.Deployment;
            int? target=own ? ownSeat : view.Phase==Phase.Action || view.Phase==Phase.EffectChoice ? view.ActiveSeat : null;
            return target.HasValue ? view.Units.FirstOrDefault(u=>u.Kind=="hero" && u.Seat==target)?.Position : null;
        }
    }
}

using Goa2.Domain;
namespace Goa2.Presentation.UI3D
{
    // Observe public phase / local chooser only, never another player's hidden selection.
    public sealed class InteractionAudioState
    {
        private string match="",key="";
        private int seat=-1;
        public string Observe(GameView view,int ownSeat)
        {
            string next=view.Pending!=null ? "pending:"+view.Pending.Id : view.Round+":"+view.Turn+":"+view.Phase+":"+view.ActiveSeat;
            bool baseline=match!=view.MatchId || seat!=ownSeat;
            bool changed=next!=key;match=view.MatchId;seat=ownSeat;key=next;
            if(baseline || !changed)return "";
            if(view.Pending!=null)return view.Pending.ChooserSeat==ownSeat?"response":"";
            if(view.Phase==Phase.Finished)return "victory";
            if(view.Phase==Phase.Action && view.ActiveSeat==ownSeat)return "turn";
            if(view.Phase==Phase.Planning)return "planning";
            return "";
        }
    }
}

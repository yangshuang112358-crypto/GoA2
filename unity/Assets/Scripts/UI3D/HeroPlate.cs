#nullable enable
using System;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation.UI3D
{
    // Public seat projection only. A card-colour meter, never hit points.
    public sealed class HeroPlate : VisualElement
    {
        public static readonly string[] Colors={"gold","silver","red","green","blue"};
        public static int Status(GameView view,PlayerView player,string color,ContentCatalog catalog,int ownSeat)
        {
            if(player.Seat==ownSeat) {
                var card=view.OwnCards.FirstOrDefault(c=>catalog.Card(c.CardId).Color==color);
                if(card!=null) return card.Zone==CardZone.Discarded ? 2 : card.Zone==CardZone.Selected || card.Zone==CardZone.PlayedResolved || card.Zone==CardZone.PlayedUnresolved ? 1 : 0;
            }
            if(player.DiscardColors.Contains(color))return 2;
            return player.Revealed.Any(c=>catalog.Card(c.CardId).Color==color) ? 1 : 0;
        }
        private readonly PlayerView player;
        private readonly int[] states;
        private readonly Label title;
        private readonly LevelPreview.Motion progress;
        public HeroPlate(ContentCatalog catalog,GameView view,PlayerView player,int ownSeat,LevelPreview.Motion? motion=null)
        {
            progress=motion ?? new LevelPreview.Motion();
            int forecast=LevelPreview.Target(player.Level,player.Gold)-player.Level;
            int count=view.RoundEndStage=="upgrades" ? player.Seat==ownSeat ? UpgradeWheelLayout.Remaining(view,ownSeat) : view.UpgradingSeats.Contains(player.Seat) ? progress.Count : 0 : forecast;
            progress.Observe(count,Time.realtimeSinceStartup);
            name="hero-plate-"+(player.Seat+1);this.player=player;states=Colors.Select(c=>Status(view,player,c,catalog,ownSeat)).ToArray();
            pickingMode=PickingMode.Ignore;style.position=Position.Absolute;style.width=224;style.height=92;
            title=new Label(catalog.Heroes.FirstOrDefault(h=>h.Id==player.HeroId)?.Name ?? "英雄") {pickingMode=PickingMode.Ignore};
            title.style.fontSize=23;title.style.color=Color.white;title.style.unityTextAlign=TextAnchor.MiddleCenter;
            title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.backgroundColor=Color.clear;title.style.unityTextOutlineWidth=.25f;title.style.unityTextOutlineColor=Color.black;title.style.height=35;Add(title);
            var level=new Label(player.Level.ToString()) {pickingMode=PickingMode.Ignore};level.style.position=Position.Absolute;
            level.style.left=4;level.style.top=38;level.style.width=42;level.style.height=42;level.style.fontSize=26;
            level.style.marginLeft=0;level.style.marginRight=0;level.style.marginTop=0;level.style.marginBottom=0;level.style.paddingLeft=0;level.style.paddingRight=0;level.style.paddingTop=0;level.style.paddingBottom=0;level.style.color=Color.white;level.style.unityTextAlign=TextAnchor.MiddleCenter;Add(level);
            if(forecast>0){
                var preview=new Label("→ "+LevelPreview.Target(player.Level,player.Gold)){name="level-preview",pickingMode=PickingMode.Ignore};
                preview.style.position=Position.Absolute;preview.style.left=48;preview.style.top=28;preview.style.fontSize=19;
                preview.style.color=new Color(1,.82f,.35f);preview.style.unityFontStyleAndWeight=FontStyle.Bold;
                preview.style.unityTextOutlineColor=Color.black;preview.style.unityTextOutlineWidth=1;Add(preview);
                tooltip="等级 "+player.Level+" → "+LevelPreview.Target(player.Level,player.Gold)+"（轮末升级，本轮仍为"+player.Level+"级）";
            }
            generateVisualContent+=Draw;
            if(player.Level>=8 || progress.Count>0)schedule.Execute(MarkDirtyRepaint).Every(32);
        }
        private void Draw(MeshGenerationContext c)
        {
            var p=c.painter2D;bool max=player.Level>=8;float t=Time.realtimeSinceStartup;
            if(progress.Count>0){
                float age=t-progress.Changed;
                float y=age<.5f ? Mathf.Lerp(79,38,age/.5f) : age<1.05f ? Mathf.Lerp(79,59,(age-.5f)/.55f) : 59;
                float pulse=age<.25f ? .78f+.22f*Mathf.Sin(age/.25f*Mathf.PI*.5f) : 1;
                float size=(progress.Count>=3 ? 12 : 8)*pulse;
                p.fillColor=new Color(1,.78f,.22f);p.strokeColor=new Color(.2f,.12f,.02f);p.lineWidth=1.5f;
                int count=progress.Count==2?2:1;
                for(int i=0;i<count;i++){
                    float x=-13,cy=y+i*10-(count-1)*5;
                    p.BeginPath();p.MoveTo(new Vector2(x,cy-size));p.LineTo(new Vector2(x+size,cy));
                    p.LineTo(new Vector2(x+size*.4f,cy));p.LineTo(new Vector2(x+size*.4f,cy+size*.65f));
                    p.LineTo(new Vector2(x-size*.4f,cy+size*.65f));p.LineTo(new Vector2(x-size*.4f,cy));p.LineTo(new Vector2(x-size,cy));p.ClosePath();p.Fill();p.Stroke();
                }
            }
            p.fillColor=new Color(.035f,.04f,.065f,.95f);p.BeginPath();p.Arc(new Vector2(25,61),21,0,360);p.Fill();
            // Inner beveled rim remains separate from the segmented experience ring.
            p.strokeColor=new Color(.62f,.67f,.73f,.65f);p.lineWidth=1;p.BeginPath();p.Arc(new Vector2(25,61),15.5f,200,340);p.Stroke();
            p.strokeColor=new Color(.04f,.06f,.09f);p.BeginPath();p.Arc(new Vector2(25,61),15.5f,20,160);p.Stroke();
            int n=Mathf.Max(1,Mathf.Min(7,player.Level));
            for(int i=0;i<n;i++) {
                p.strokeColor=max ? new Color(.75f,.36f,1) : i<Mathf.Min(player.Gold,n) ? new Color(1,.77f,.27f) : new Color(.34f,.37f,.41f);
                p.lineWidth=4;p.BeginPath();p.Arc(new Vector2(25,61),19,-90+i*360f/n+3,-90+(i+1)*360f/n-3);p.Stroke();
            }
            var colors=new[]{"#e4c17d","#c5d1d8","#df667a","#62c996","#599eef"};
            for(int i=0;i<5;i++) {
                var hue=Board3DScene.ColorOf(colors[i]);p.fillColor=states[i]==2 ? new Color(.3f,.32f,.35f) : states[i]==1 ? new Color(hue.r*.42f,hue.g*.42f,hue.b*.42f) : hue;
                Rect(p,51+i*33,49,31,31);p.Fill();
                p.fillColor=new Color(1,1,1,.16f);Rect(p,51+i*33,49,31,4);p.Fill();
                p.fillColor=new Color(0,0,0,.25f);Rect(p,51+i*33,76,31,4);p.Fill();
            }
            p.strokeColor=max ? new Color(.7f+.15f*Mathf.Sin(t*3),.24f,.95f) : player.Team==Team.Blue ? new Color(.3f,.65f,1) : new Color(1,.35f,.35f);
            p.lineWidth=max ? 3 : 1.5f;Rect(p,49,47,168,35);p.Stroke();
            if(max) for(int i=0;i<9;i++) {
                float a=i*Mathf.PI*2/9;var start=new Vector2(25+Mathf.Cos(a)*21,61+Mathf.Sin(a)*21);
                var end=start+new Vector2(Mathf.Sin(t*3+i)*3,-7-5*Mathf.Sin(t*4+i));
                p.strokeColor=new Color(.8f,.4f,1,.7f);p.lineWidth=2;p.BeginPath();p.MoveTo(start);p.QuadraticCurveTo((start+end)*.5f+Vector2.right*4,end);p.Stroke();
            }
        }
        private static void Rect(Painter2D p,float x,float y,float w,float h) {p.BeginPath();p.MoveTo(new Vector2(x,y));p.LineTo(new Vector2(x+w,y));p.LineTo(new Vector2(x+w,y+h));p.LineTo(new Vector2(x,y+h));p.ClosePath();}
    }
}

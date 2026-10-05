#nullable enable
using System;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation.UI3D
{
    // Public seat projection only. Five card colours, never hit points.
    public sealed class HeroPlate : VisualElement
    {
        public const float Width=246,Height=108;
        public static readonly string[] Colors={"gold","silver","red","green","blue"};
        public static int Status(GameView view,PlayerView player,string color,ContentCatalog catalog,int ownSeat)
        {
            if(player.Seat==ownSeat) {
                var card=view.OwnCards.FirstOrDefault(c=>catalog.Card(c.CardId).Color==color);
                if(card!=null)return card.Zone==CardZone.Discarded?2:card.Zone==CardZone.Selected || card.Zone==CardZone.PlayedResolved || card.Zone==CardZone.PlayedUnresolved?1:0;
            }
            if(player.DiscardColors.Contains(color))return 2;
            return player.Revealed.Any(c=>catalog.Card(c.CardId).Color==color)?1:0;
        }
        private readonly PlayerView player;
        private readonly int[] states;
        private readonly LevelPreview.Motion progress;
        private readonly Func<int> visibleGold;
        private readonly Label level;
        private readonly bool upgrading;
        private static readonly Vector2 Circle=new Vector2(35,70);
        public HeroPlate(ContentCatalog catalog,GameView view,PlayerView player,int ownSeat,LevelPreview.Motion? motion=null,Func<int>? visibleGold=null)
        {
            this.player=player;this.visibleGold=visibleGold??(()=>player.Gold);upgrading=view.RoundEndStage=="upgrades";
            progress=motion??new LevelPreview.Motion();
            int count=upgrading?player.Seat==ownSeat?UpgradeWheelLayout.Remaining(view,ownSeat):view.UpgradingSeats.Contains(player.Seat)?progress.Count:0:LevelPreview.Target(player.Level,this.visibleGold())-player.Level;
            progress.Observe(count,Time.realtimeSinceStartup);
            name="hero-plate-"+(player.Seat+1);states=Colors.Select(c=>Status(view,player,c,catalog,ownSeat)).ToArray();
            pickingMode=PickingMode.Ignore;style.position=Position.Absolute;style.width=Width;style.height=Height;
            var title=new Label(catalog.Heroes.FirstOrDefault(h=>h.Id==player.HeroId)?.Name??"英雄"){pickingMode=PickingMode.Ignore};
            title.style.fontSize=23;title.style.color=Color.white;title.style.unityTextAlign=TextAnchor.MiddleCenter;title.style.height=34;
            title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.unityTextOutlineWidth=.5f;title.style.unityTextOutlineColor=Color.black;Add(title);
            level=new Label{name="level-preview",pickingMode=PickingMode.Ignore};level.style.position=Position.Absolute;
            level.style.left=9;level.style.top=44;level.style.width=52;level.style.height=52;level.style.fontSize=23;
            level.style.marginLeft=level.style.marginRight=level.style.marginTop=level.style.marginBottom=0;
            level.style.paddingLeft=level.style.paddingRight=level.style.paddingTop=level.style.paddingBottom=0;
            level.style.unityTextAlign=TextAnchor.MiddleCenter;level.style.unityFontStyleAndWeight=FontStyle.Bold;
            level.style.unityTextOutlineWidth=.6f;level.style.unityTextOutlineColor=Color.black;Add(level);
            Refresh();generateVisualContent+=Draw;schedule.Execute(Refresh).Every(32);
        }
        private void Refresh()
        {
            var next=LevelPreview.Progress(player.Level,visibleGold());bool advance=next.Level>player.Level;
            level.text=advance?player.Level+"→"+next.Level:player.Level.ToString();
            level.style.color=advance?new Color(1,.84f,.43f):Color.white;
            tooltip="本轮等级 "+player.Level+(advance?" · 轮末可升至 "+next.Level:"")+(next.Cost>0?" · 再升一级经验 "+next.Gold+" / "+next.Cost:" · 满级");
            if(!upgrading)progress.Observe(next.Level-player.Level,Time.realtimeSinceStartup);
            MarkDirtyRepaint();
        }
        private void Draw(MeshGenerationContext c)
        {
            var p=c.painter2D;bool max=player.Level>=8;float time=Time.realtimeSinceStartup;
            var rim=max?new Color(.74f+.12f*Mathf.Sin(time*3),.28f,1):player.Team==Team.Blue?new Color(.35f,.67f,.88f):new Color(.9f,.43f,.39f);
            // One continuous brass silhouette: large circular boss blends into a low band.
            p.fillColor=new Color(.09f,.10f,.13f,.97f);Outline(p);p.Fill();
            p.strokeColor=new Color(.055f,.035f,.022f);p.lineWidth=5;Outline(p);p.Stroke();
            p.strokeColor=new Color(.57f,.47f,.30f);p.lineWidth=2.5f;Outline(p);p.Stroke();
            p.strokeColor=rim;p.lineWidth=max?2.8f:1.2f;Outline(p);p.Stroke();
            // Readable bevel at the shoulder of the joined band.
            p.strokeColor=new Color(.91f,.81f,.58f,.6f);p.lineWidth=1.2f;p.BeginPath();p.MoveTo(new Vector2(65,58));p.QuadraticCurveTo(new Vector2(78,68),new Vector2(117,67));p.LineTo(new Vector2(239,67));p.Stroke();
            var colors=new[]{"#e4c17d","#c5d1d8","#df667a","#62c996","#599eef"};
            for(int i=0;i<5;i++) {
                var hue=Board3DScene.ColorOf(colors[i]);float x=68+i*34;
                p.fillColor=states[i]==2?new Color(.30f,.32f,.35f):states[i]==1?new Color(hue.r*.42f,hue.g*.42f,hue.b*.42f):hue;
                Rect(p,x,72,32,26);p.Fill();
                p.fillColor=new Color(1,1,1,states[i]==0?.23f:.09f);Rect(p,x+1,73,30,3);p.Fill();
                p.fillColor=new Color(0,0,0,.3f);Rect(p,x,95,32,3);p.Fill();
                p.strokeColor=new Color(.055f,.055f,.065f);p.lineWidth=1;Rect(p,x,72,32,26);p.Stroke();
            }
            p.fillColor=new Color(.035f,.055f,.08f);p.BeginPath();p.Arc(Circle,26,0,360);p.Fill();
            p.strokeColor=new Color(.72f,.66f,.5f);p.lineWidth=1;p.BeginPath();p.Arc(Circle,24,190,350);p.Stroke();
            p.strokeColor=new Color(.015f,.025f,.04f);p.BeginPath();p.Arc(Circle,24,10,170);p.Stroke();
            var next=LevelPreview.Progress(player.Level,visibleGold());int n=Math.Max(1,next.Cost);
            for(int i=0;i<n;i++) {
                p.strokeColor=max?new Color(.75f,.36f,1):i<next.Gold?new Color(1,.78f,.28f):new Color(.31f,.34f,.37f);
                p.lineWidth=4;p.BeginPath();p.Arc(Circle,29,-90+i*360f/n+3,-90+(i+1)*360f/n-3);p.Stroke();
            }
            if(progress.Count>0){
                float age=time-progress.Changed,y=LevelPreview.ArrowY(age),alpha=LevelPreview.ArrowOpacity(age);
                float pulse=age<.3f?1-.23f*Mathf.Sin(Mathf.Clamp01(age/.3f)*Mathf.PI):1;
                float size=(progress.Count>=3?12:9)*pulse;
                p.fillColor=new Color(1,.80f,.28f,alpha);p.strokeColor=new Color(.23f,.15f,.035f,alpha);p.lineWidth=1.5f;
                int count=progress.Count==2?2:1;
                for(int i=0;i<count;i++){
                    float x=-14,cy=y+i*10-(count-1)*5;
                    p.BeginPath();p.MoveTo(new Vector2(x,cy-size));p.LineTo(new Vector2(x+size,cy));p.LineTo(new Vector2(x+size*.4f,cy));
                    p.LineTo(new Vector2(x+size*.4f,cy+size*.65f));p.LineTo(new Vector2(x-size*.4f,cy+size*.65f));p.LineTo(new Vector2(x-size*.4f,cy));p.LineTo(new Vector2(x-size,cy));p.ClosePath();p.Fill();p.Stroke();
                }
            }
            if(max)for(int i=0;i<12;i++){
                float a=i*Mathf.PI*2/12;var start=Circle+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*32;
                var end=start+new Vector2(Mathf.Sin(time*3+i)*3,-7-5*Mathf.Sin(time*4+i));
                p.strokeColor=new Color(.8f,.4f,1,.65f);p.lineWidth=2;p.BeginPath();p.MoveTo(start);p.QuadraticCurveTo((start+end)*.5f+Vector2.right*4,end);p.Stroke();
            }
        }
        private static void Outline(Painter2D p)
        {
            p.BeginPath();p.MoveTo(new Vector2(35,38));
            p.BezierCurveTo(new Vector2(55,38),new Vector2(60,54),new Vector2(72,61));
            p.BezierCurveTo(new Vector2(94,72),new Vector2(164,63),new Vector2(242,64));
            p.LineTo(new Vector2(242,102));p.LineTo(new Vector2(35,102));
            p.BezierCurveTo(new Vector2(-8,102),new Vector2(-8,38),new Vector2(35,38));p.ClosePath();
        }
        private static void Rect(Painter2D p,float x,float y,float w,float h){p.BeginPath();p.MoveTo(new Vector2(x,y));p.LineTo(new Vector2(x+w,y));p.LineTo(new Vector2(x+w,y+h));p.LineTo(new Vector2(x,y+h));p.ClosePath();}
    }
}

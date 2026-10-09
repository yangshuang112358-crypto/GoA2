using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation.UI3D
{
    // One footprint for planning, defense, discard, browsing and upgrade choices.
    // Texture geometry comes from art/production/assets/CarvedUI.blend; values remain live UI.
    public static class SkillDiscArtwork
    {
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        public static Rect BadgeRect(string slot) => slot switch {
            "movement" => new Rect(8,17,44,46),
            "defense" => new Rect(104,17,44,46),
            "primary" => new Rect(8,85,44,46),
            "range" => new Rect(104,85,44,46),
            "initiative" => new Rect(56,109,44,46),
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
        public static Texture2D Texture(string kind)
        {
            if (!textures.TryGetValue(kind, out var texture) || texture == null)
                textures[kind] = texture = Resources.Load<Texture2D>("UI3D/SkillDiscs/" + kind);
            return texture;
        }
        public static Color ValueColor(int bonus) => bonus > 0 ? new Color(.35f,1,.55f) : bonus < 0 ? new Color(1,.32f,.35f) : Color.white;
        public static Texture2D R2B(string key){
            string cache="r2b/"+key;
            if(!textures.TryGetValue(cache,out var texture))textures[cache]=texture=Resources.Load<Texture2D>("UI3D/R2B/"+key);
            return texture;
        }
        public static string Semantic(string kind)=>kind=="spark"?"skill":kind=="arrow"?"distance":kind;
        public static string ValueKey(string value)=>value=="∞"?"inf":int.TryParse(value,out int n)?(n<0?"n"+(-n):"p"+n):"none";
        public static Color StoneColor(string color)=>color switch{
            "gold"=>new Color(1,.76f,.32f),"silver"=>new Color(.93f,.97f,1),
            "red"=>new Color(1,.46f,.32f),"green"=>new Color(.37f,.83f,.51f),
            "blue"=>new Color(.36f,.65f,1),"purple"=>new Color(.78f,.39f,1),_=>Color.white};
        public static void Quad(MeshGenerationContext context,Texture2D texture,Rect rect,Func<Vector2,Vector2> project,Color tint){
            if(texture==null)return;var mesh=context.Allocate(4,6,texture);var uv=mesh.uvRegion;
            var points=new[]{new Vector2(rect.xMin,rect.yMin),new Vector2(rect.xMax,rect.yMin),new Vector2(rect.xMax,rect.yMax),new Vector2(rect.xMin,rect.yMax)};
            var uvs=new[]{new Vector2(uv.xMin,uv.yMax),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMax,uv.yMin),new Vector2(uv.xMin,uv.yMin)};
            for(int i=0;i<4;i++){points[i]=project(points[i]);mesh.SetNextVertex(new Vertex{position=new Vector3(points[i].x,points[i].y,Vertex.nearZ),uv=uvs[i],tint=tint});}
            var a=points[1]-points[0];var b=points[2]-points[0];
            foreach(ushort i in a.x*b.y-a.y*b.x<0?new ushort[]{0,2,1,2,0,3}:new ushort[]{0,1,2,2,3,0})mesh.SetNextIndex(i);
        }
        public static void Draw(MeshGenerationContext context, string kind, Func<Vector2,Vector2> project, Color tint)
        {
            var texture = Texture(kind);
            if(texture == null)return;
            var mesh = context.Allocate(4,6,texture);
            var uv = mesh.uvRegion;
            Vector2[] points = {new Vector2(-78,-78),new Vector2(78,-78),new Vector2(78,78),new Vector2(-78,78)};
            Vector2[] projected = new Vector2[4];
            Vector2[] uvs = {new Vector2(uv.xMin,uv.yMax),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMax,uv.yMin),new Vector2(uv.xMin,uv.yMin)};
            for(int i=0;i<4;i++) {
                Vector2 p = projected[i] = project(points[i]);
                mesh.SetNextVertex(new Vertex {position=new Vector3(p.x,p.y,Vertex.nearZ),tint=tint,uv=uvs[i]});
            }
            // A discarded card rotates through 180 degrees. Keep the reverse visible
            // with the UI mesh's front winding; its shader culls reversed triangles.
            Vector2 a=projected[1]-projected[0],b=projected[2]-projected[0];
            var indices=a.x*b.y-a.y*b.x<0 ? new ushort[]{0,2,1,2,0,3} : new ushort[]{0,1,2,2,3,0};
            foreach(ushort i in indices)mesh.SetNextIndex(i);
        }
    }

    public sealed class SkillBadge : VisualElement
    {
        public readonly bool HasCarvedValue;
        public readonly Texture2D CarvedTexture;
        public SkillBadge(string kind,string value,int bonus,string cardColor="")
        {
            bool speed=kind=="hourglass";
            float width=44,height=46;
            pickingMode=PickingMode.Ignore;style.position=Position.Absolute;
            style.width=width;style.height=height;style.overflow=Overflow.Hidden;
            string semantic=SkillDiscArtwork.Semantic(kind),key=semantic+"-"+SkillDiscArtwork.ValueKey(value);
            CarvedTexture=SkillDiscArtwork.R2B(key);HasCarvedValue=CarvedTexture!=null;
            var face=CarvedTexture??SkillDiscArtwork.R2B(semantic+"-none");
            var emblem=SkillDiscArtwork.R2B(semantic+"-emblem");var fill=SkillDiscArtwork.R2B(key+"-fill");
            if(face!=null)generateVisualContent+=context=>{
                var r=new Rect(0,0,width,height);
                SkillDiscArtwork.Quad(context,face,r,v=>v,SkillDiscArtwork.StoneColor(cardColor));
                SkillDiscArtwork.Quad(context,emblem,r,v=>v,Color.white);
                if(bonus!=0 && fill!=null)SkillDiscArtwork.Quad(context,fill,r,v=>v,SkillDiscArtwork.ValueColor(bonus));
            };
            else style.backgroundImage=new StyleBackground(SkillDiscArtwork.Texture(speed?"speed":kind));
            tooltip=kind switch {"boot"=>"移动","shield"=>"防御","sword"=>"攻击","range"=>"范围","arrow"=>"远程","hourglass"=>"先攻",_=>"技能"};
            var text=new Label(value){name="badge-number",pickingMode=PickingMode.Ignore};
            text.style.position=Position.Absolute;text.style.left=0;text.style.top=19;
            text.style.width=width;text.style.height=24;
            text.style.fontSize=21;text.style.unityTextAlign=TextAnchor.MiddleCenter;
            text.style.unityFontStyleAndWeight=FontStyle.Bold;text.style.whiteSpace=WhiteSpace.NoWrap;
            text.style.color=SkillDiscArtwork.ValueColor(bonus);
            // Keep the exact semantic string available for accessibility/audits.
            // Visible digits are sculpted cavities; novel values remain readable.
            text.style.opacity=HasCarvedValue?0:1;
            text.style.unityTextOutlineColor=new Color(.055f,.045f,.035f);text.style.unityTextOutlineWidth=1.1f;
            text.style.marginLeft=0;text.style.marginRight=0;text.style.marginTop=0;text.style.marginBottom=0;
            text.style.paddingLeft=0;text.style.paddingRight=0;text.style.paddingTop=0;text.style.paddingBottom=0;
            // Zero-valued skills retain their engraved action symbol, with no fake zero.
            Add(text);
        }
    }
}

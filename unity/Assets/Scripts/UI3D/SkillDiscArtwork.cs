using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation.UI3D
{
    // One footprint for planning, defense, discard, browsing and upgrade choices.
    // Texture geometry comes from art/ui/SkillDiscs.blend; values remain live UI.
    public static class SkillDiscArtwork
    {
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        public static Rect BadgeRect(string slot) => slot switch {
            "movement" => new Rect(17,24,34,38),
            "defense" => new Rect(105,24,34,38),
            "primary" => new Rect(17,84,34,38),
            "range" => new Rect(105,84,34,38),
            "initiative" => new Rect(50,115,56,36),
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
        public static Texture2D Texture(string kind)
        {
            if (!textures.TryGetValue(kind, out var texture) || texture == null)
                textures[kind] = texture = Resources.Load<Texture2D>("UI3D/SkillDiscs/" + kind);
            return texture;
        }
        public static Color ValueColor(int bonus) => bonus > 0 ? new Color(.35f,1,.55f) : bonus < 0 ? new Color(1,.32f,.35f) : Color.white;
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
        public SkillBadge(string kind,string value,int bonus)
        {
            bool speed=kind=="hourglass";
            float width=speed?56:34,height=speed?36:38;
            pickingMode=PickingMode.Ignore;style.position=Position.Absolute;
            style.width=width;style.height=height;style.overflow=Overflow.Hidden;
            style.backgroundImage=new StyleBackground(SkillDiscArtwork.Texture(speed?"speed":kind));
            tooltip=kind switch {"boot"=>"移动","shield"=>"防御","sword"=>"攻击","range"=>"范围","arrow"=>"远程","hourglass"=>"先攻",_=>"技能"};
            var text=new Label(value){name="badge-number",pickingMode=PickingMode.Ignore};
            text.style.position=Position.Absolute;text.style.left=0;text.style.top=speed?1:12;
            text.style.width=width;text.style.height=speed?34:25;
            text.style.fontSize=speed?27:22;text.style.unityTextAlign=TextAnchor.MiddleCenter;
            text.style.unityFontStyleAndWeight=FontStyle.Bold;text.style.whiteSpace=WhiteSpace.NoWrap;
            text.style.color=SkillDiscArtwork.ValueColor(bonus);
            text.style.unityTextOutlineColor=new Color(.055f,.045f,.035f);text.style.unityTextOutlineWidth=1.1f;
            text.style.marginLeft=0;text.style.marginRight=0;text.style.marginTop=0;text.style.marginBottom=0;
            text.style.paddingLeft=0;text.style.paddingRight=0;text.style.paddingTop=0;text.style.paddingBottom=0;
            // Zero-valued skills retain their engraved action symbol, with no fake zero.
            Add(text);
        }
    }
}

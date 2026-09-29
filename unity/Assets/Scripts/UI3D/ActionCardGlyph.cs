#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation.UI3D
{
    // Replaceable code-native art. Portraits are stylized identifiers, not official illustrations.
    public sealed class ActionCardGlyph : VisualElement
    {
        private readonly string kind;private readonly Color tint;private readonly bool portrait,framed;
        public ActionCardGlyph(string kind,Color tint,float size,bool portrait=false,bool framed=false)
        {
            this.kind=kind;this.tint=tint;this.portrait=portrait;this.framed=framed;
            pickingMode=PickingMode.Ignore;style.width=size;style.height=size;style.flexShrink=0;
            generateVisualContent+=Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D;float s=contentRect.width/64;
            Vector2 V(float x,float y)=>new Vector2(x*s,y*s);
            void Poly(Color color,params Vector2[] points){p.fillColor=color;p.BeginPath();p.MoveTo(points[0]*s);for(int i=1;i<points.Length;i++)p.LineTo(points[i]*s);p.ClosePath();p.Fill();}
            void Line(Color color,float width,params Vector2[] points){p.strokeColor=color;p.lineWidth=width*s;p.BeginPath();p.MoveTo(points[0]*s);for(int i=1;i<points.Length;i++)p.LineTo(points[i]*s);p.Stroke();}
            void Circle(float x,float y,float r,Color color){p.fillColor=color;p.BeginPath();p.Arc(V(x,y),r*s,0,360);p.Fill();}
            Color light=new Color(.95f,.86f,.63f),dark=new Color(.07f,.09f,.11f);
            if(portrait)
            {
                Circle(32,32,31,tint);Circle(32,32,28,dark);Circle(32,32,25,new Color(.22f,.25f,.28f));
                Color hair=kind=="arien"?new Color(.2f,.68f,.76f):kind=="wasp"?new Color(.78f,.63f,.28f):kind=="tigerclaw"?new Color(.73f,.41f,.18f):kind=="shargatha"?new Color(.46f,.34f,.59f):new Color(.28f,.22f,.17f);
                Color skin=kind=="shargatha"?new Color(.47f,.62f,.47f):kind=="arien"?new Color(.52f,.74f,.77f):new Color(.75f,.58f,.43f);
                Poly(hair,new Vector2(8,52),new Vector2(15,43),new Vector2(26,39),new Vector2(39,39),new Vector2(51,46),new Vector2(56,54),new Vector2(32,60));
                Poly(hair,new Vector2(16,39),new Vector2(14,24),new Vector2(19,12),new Vector2(33,7),new Vector2(47,16),new Vector2(50,38));
                Poly(skin,new Vector2(22,23),new Vector2(40,20),new Vector2(44,35),new Vector2(34,46),new Vector2(24,39));
                Poly(hair,new Vector2(17,24),new Vector2(22,12),new Vector2(36,10),new Vector2(46,20),new Vector2(35,18),new Vector2(26,26));
                Line(dark,2,new Vector2(25,30),new Vector2(29,29));Line(dark,2,new Vector2(36,29),new Vector2(40,30));
                Line(light,1,new Vector2(34,32),new Vector2(33,36));Line(dark,1,new Vector2(30,39),new Vector2(36,39));
                if(kind=="brogan"){Poly(new Color(.5f,.57f,.61f),new Vector2(15,26),new Vector2(18,13),new Vector2(44,13),new Vector2(48,26),new Vector2(36,22),new Vector2(32,28),new Vector2(27,22));Line(light,2,new Vector2(32,12),new Vector2(32,27));}
                if(kind=="tigerclaw"){Poly(hair,new Vector2(15,23),new Vector2(12,6),new Vector2(27,17));Poly(hair,new Vector2(38,16),new Vector2(51,7),new Vector2(47,26));Line(dark,3,new Vector2(20,30),new Vector2(26,34));Line(dark,3,new Vector2(43,30),new Vector2(39,34));}
                if(kind=="wasp"){Line(light,3,new Vector2(17,24),new Vector2(10,12),new Vector2(19,18));Line(light,3,new Vector2(46,22),new Vector2(54,10),new Vector2(45,16));}
                if(kind=="shargatha"){for(int i=0;i<4;i++){float x=15+i*11;Line(hair,4,new Vector2(x,24),new Vector2(x-4,12),new Vector2(x+3,6));Circle(x+3,6,2,light);}}
                if(kind=="sabina"){Poly(new Color(.27f,.18f,.12f),new Vector2(13,22),new Vector2(22,13),new Vector2(41,13),new Vector2(51,22));Line(light,2,new Vector2(21,22),new Vector2(44,22));Circle(27,28,4,dark);Circle(38,28,4,dark);}
                if(kind=="arien")Line(light,2,new Vector2(23,21),new Vector2(27,15),new Vector2(32,21),new Vector2(38,14),new Vector2(42,22));
                return;
            }
            if(framed){Poly(tint,new Vector2(9,2),new Vector2(55,2),new Vector2(62,9),new Vector2(62,55),new Vector2(55,62),new Vector2(9,62),new Vector2(2,55),new Vector2(2,9));Poly(dark,new Vector2(10,6),new Vector2(54,6),new Vector2(58,10),new Vector2(58,54),new Vector2(54,58),new Vector2(10,58),new Vector2(6,54),new Vector2(6,10));}
            Color ink=framed?light:tint;
            switch(kind)
            {
                case "attack": Poly(ink,new Vector2(15,52),new Vector2(11,48),new Vector2(25,33),new Vector2(17,25),new Vector2(22,20),new Vector2(29,27),new Vector2(48,8),new Vector2(54,7),new Vector2(51,17),new Vector2(35,34),new Vector2(42,41),new Vector2(37,46),new Vector2(29,39));break;
                case "defense": Poly(ink,new Vector2(11,15),new Vector2(32,7),new Vector2(53,15),new Vector2(49,39),new Vector2(32,57),new Vector2(15,39));Line(dark,3,new Vector2(32,13),new Vector2(32,47));break;
                case "movement": Poly(ink,new Vector2(21,8),new Vector2(42,8),new Vector2(38,36),new Vector2(51,44),new Vector2(52,54),new Vector2(11,54),new Vector2(11,44),new Vector2(23,34));Line(dark,3,new Vector2(23,21),new Vector2(37,21));break;
                case "initiative": Line(ink,5,new Vector2(15,10),new Vector2(49,10));Line(ink,5,new Vector2(15,54),new Vector2(49,54));Line(ink,4,new Vector2(20,12),new Vector2(22,23),new Vector2(40,41),new Vector2(44,52));Line(ink,4,new Vector2(44,12),new Vector2(42,23),new Vector2(24,41),new Vector2(20,52));break;
                case "ranged": Line(ink,5,new Vector2(12,52),new Vector2(49,15));Line(ink,5,new Vector2(31,15),new Vector2(49,15),new Vector2(49,34));break;
                case "range": p.strokeColor=ink;p.lineWidth=3*s;p.BeginPath();p.Arc(V(32,32),20*s,0,360);p.Stroke();Line(ink,3,new Vector2(32,4),new Vector2(32,21));Line(ink,3,new Vector2(32,43),new Vector2(32,60));Line(ink,3,new Vector2(4,32),new Vector2(21,32));Line(ink,3,new Vector2(43,32),new Vector2(60,32));break;
                case "discard": Line(ink,6,new Vector2(32,8),new Vector2(32,43));Line(ink,6,new Vector2(18,31),new Vector2(32,45),new Vector2(46,31));Line(ink,4,new Vector2(12,47),new Vector2(12,56),new Vector2(52,56),new Vector2(52,47));break;
                case "recover": Line(ink,5,new Vector2(32,55),new Vector2(32,15));Line(ink,5,new Vector2(17,30),new Vector2(32,15),new Vector2(47,30));break;
                case "reaction": Line(ink,5,new Vector2(16,17),new Vector2(45,17),new Vector2(51,32),new Vector2(43,47),new Vector2(13,47));Line(ink,5,new Vector2(26,34),new Vector2(13,47),new Vector2(26,59));break;
                default: Poly(ink,new Vector2(32,5),new Vector2(39,25),new Vector2(59,32),new Vector2(39,39),new Vector2(32,59),new Vector2(25,39),new Vector2(5,32),new Vector2(25,25));break;
            }
        }
    }
}

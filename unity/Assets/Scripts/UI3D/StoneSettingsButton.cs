using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation.UI3D
{
    // The hit rectangle stays still; only the drawn stone and its inscription tilt.
    public sealed class StoneSettingsButton : Button
    {
        public sealed class Motion
        {
            public float Hover, Press, Velocity, GearAngle;
            public Vector2 Tilt;
        }

        private readonly Motion motion;
        private readonly bool open;
        private readonly Label inscription;
        private bool hovered, held, focused;
        private Vector2 pointerTilt;
        private float lastTime = Time.realtimeSinceStartup;
        public float HoverAmount => motion.Hover;
        public float PressAmount => motion.Press;

        public StoneSettingsButton(Action clicked, bool open, Motion motion) : base(clicked)
        {
            this.open = open;
            this.motion = motion;
            name = "settings-toggle";
            tooltip = "设置：行动 / 调试 / 热键";
            AddToClassList("stone-settings");
            inscription = new Label("设置") { pickingMode = PickingMode.Ignore };
            inscription.style.position = Position.Absolute;
            inscription.style.left = 0;
            inscription.style.top = 44;
            inscription.style.width = 104;
            inscription.style.height = 24;
            inscription.style.marginTop = inscription.style.marginBottom = 0;
            inscription.style.marginLeft = inscription.style.marginRight = 0;
            inscription.style.paddingTop = inscription.style.paddingBottom = 0;
            inscription.style.fontSize = 20;
            inscription.style.unityTextAlign = TextAnchor.MiddleCenter;
            inscription.style.unityFontStyleAndWeight = FontStyle.Bold;
            inscription.style.color = new Color(1, .88f, .65f);
            inscription.style.unityTextOutlineColor = new Color(.09f, .07f, .04f);
            inscription.style.unityTextOutlineWidth = .6f;
            Add(inscription);
            PositionInscription();
            RegisterCallback<PointerEnterEvent>(_ => hovered = true);
            RegisterCallback<PointerMoveEvent>(e => {
                hovered = true;
                pointerTilt = new Vector2(Mathf.Clamp((e.localPosition.x - 52) / 52, -1, 1), Mathf.Clamp((e.localPosition.y - 36) / 36, -1, 1));
            });
            RegisterCallback<PointerLeaveEvent>(_ => { hovered = false; pointerTilt = Vector2.zero; });
            RegisterCallback<PointerDownEvent>(e => { if(e.button == 0) held = true; }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(e => { if(e.button == 0) held = false; }, TrickleDown.TrickleDown);
            RegisterCallback<PointerCaptureOutEvent>(_ => held = false);
            RegisterCallback<DetachFromPanelEvent>(_ => held = false);
            RegisterCallback<FocusInEvent>(_ => focused = true);
            RegisterCallback<FocusOutEvent>(_ => { focused = false; held = false; });
            generateVisualContent += Draw;
            schedule.Execute(Animate).Every(16);
        }

        private void Animate()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Clamp(now - lastTime, 0, .035f);
            lastTime = now;
            float hoverTarget = hovered || focused ? 1 : 0;
            float pressTarget = held ? 1 : open ? .22f : 0;
            Vector2 tiltTarget = pointerTilt * hoverTarget;
            float angleTarget = open ? 18 : hoverTarget * -7;
            bool moving = Mathf.Abs(motion.Hover - hoverTarget) > .002f || Mathf.Abs(motion.Press - pressTarget) > .002f || Mathf.Abs(motion.Velocity) > .01f || Vector2.Distance(motion.Tilt, tiltTarget) > .002f || Mathf.Abs(motion.GearAngle - angleTarget) > .02f;
            if(!moving) return;
            float blend = 1 - Mathf.Exp(-13 * dt);
            motion.Hover = Mathf.Lerp(motion.Hover, hoverTarget, blend);
            motion.Tilt = Vector2.Lerp(motion.Tilt, tiltTarget, blend);
            motion.GearAngle = Mathf.Lerp(motion.GearAngle, angleTarget, blend);
            motion.Velocity += ((pressTarget - motion.Press) * 220 - motion.Velocity * 19) * dt;
            motion.Press = Mathf.Clamp(motion.Press + motion.Velocity * dt, -.08f, 1.08f);
            PositionInscription();
            MarkDirtyRepaint();
        }

        private void PositionInscription()
        {
            var labelPoint = Project(new Vector2(52, 56));
            inscription.style.translate = new Translate(labelPoint.x - 52, labelPoint.y - 56);
            inscription.style.rotate = new Rotate(motion.Tilt.x * 1.6f);
            inscription.style.scale = new Scale(new Vector3(1 + motion.Hover * .025f - motion.Press * .035f, 1 - Mathf.Abs(motion.Tilt.y) * .025f, 1));
        }

        private Vector2 Project(Vector2 point, float depth = 0)
        {
            Vector2 origin = new Vector2(52, 36);
            Vector2 local = (point - origin) * (1 + motion.Hover * .025f - motion.Press * .035f);
            var q = Quaternion.Euler(-motion.Tilt.y * 9, motion.Tilt.x * 11, 0) * new Vector3(local.x, local.y, depth);
            float perspective = 260 / (260 - q.z);
            return origin + new Vector2(q.x, q.y) * perspective + new Vector2(0, motion.Press * 4 - motion.Hover * 2.6f);
        }

        private static Vector2[] Cut(float left, float top, float right, float bottom, float cut)
        {
            return new[] {new Vector2(left+cut,top), new Vector2(right-cut,top), new Vector2(right,top+cut), new Vector2(right,bottom-cut), new Vector2(right-cut,bottom), new Vector2(left+cut,bottom), new Vector2(left,bottom-cut), new Vector2(left,top+cut)};
        }
        private void Polygon(Painter2D p, Vector2[] points, Color color, float depth = 0)
        {
            p.fillColor = color; p.BeginPath();
            for(int i=0;i<points.Length;i++) {var v=Project(points[i],depth); if(i==0)p.MoveTo(v); else p.LineTo(v);}
            p.ClosePath(); p.Fill();
        }
        private void Line(Painter2D p, Color color, float width, params Vector2[] points)
        {
            p.strokeColor=color; p.lineWidth=width; p.BeginPath();
            for(int i=0;i<points.Length;i++) {var v=Project(points[i],2); if(i==0)p.MoveTo(v); else p.LineTo(v);}
            p.Stroke();
        }
        private void Bevel(Painter2D p, Vector2[] outside, Vector2[] inside, Color light, Color dark)
        {
            for(int i=0;i<outside.Length;i++) {
                int j=(i+1)%outside.Length;
                Vector2 edge=(outside[i]+outside[j])*.5f-new Vector2(52,36);
                float lit=Mathf.Clamp01(.5f-edge.normalized.y*.4f-edge.normalized.x*.2f);
                Polygon(p,new[]{outside[i],outside[j],inside[j],inside[i]},Color.Lerp(dark,light,lit));
            }
        }
        private void Circle(Painter2D p, Vector2 center, float radius, Color color, float depth=3)
        {
            var points=new Vector2[40];
            for(int i=0;i<points.Length;i++){float a=i*Mathf.PI*2/points.Length;points[i]=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;}
            Polygon(p,points,color,depth);
        }
        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D;
            float glow=Mathf.Max(motion.Hover,open?.65f:0);
            // Shadow, dark stone sidewall, bronze rim, then the recessed stone face.
            Polygon(p,Cut(3,9,103,73,13),new Color(0,0,0,.38f));
            if(glow>.01f) {
                Polygon(p,Cut(0,0,104,72,14),new Color(1,.62f,.14f,.08f*glow));
                Polygon(p,Cut(2,2,102,70,13),new Color(1,.72f,.27f,.18f*glow));
            }
            Polygon(p,Cut(5,9,99,71,12),new Color(.12f,.105f,.09f));
            var outer=Cut(5,3,99,65,11);var metal=Cut(9,7,95,61,9);
            Polygon(p,outer,new Color(.39f,.28f,.15f));
            Bevel(p,outer,metal,new Color(.91f,.76f,.46f),new Color(.22f,.16f,.1f));
            Polygon(p,metal,new Color(.12f,.15f,.16f));
            var stone=Cut(13,11,91,59,7);
            Bevel(p,metal,stone,new Color(.48f,.52f,.5f),new Color(.18f,.22f,.23f));
            Polygon(p,stone,new Color(.29f,.34f,.35f));
            Polygon(p,new[]{new Vector2(20,11),new Vector2(69,11),new Vector2(43,35),new Vector2(13,30),new Vector2(13,18)},new Color(.4f,.45f,.45f,.4f));
            Polygon(p,new[]{new Vector2(43,35),new Vector2(91,22),new Vector2(91,52),new Vector2(84,59),new Vector2(52,59)},new Color(.13f,.18f,.2f,.3f));
            // Fixed grain: repainting never changes the texture or any rules RNG.
            for(int i=0;i<64;i++) {
                float x=20+(i*37%65),y=16+(i*19%37);
                Line(p,new Color(i%2==0?.75f:.05f,.55f,.35f,.07f),.7f,new Vector2(x,y),new Vector2(x+1.2f,y+.4f));
            }
            Line(p,new Color(.08f,.1f,.1f,.65f),1.1f,new Vector2(20,12),new Vector2(23,19),new Vector2(19,25));
            Line(p,new Color(.08f,.1f,.1f,.65f),1.1f,new Vector2(88,43),new Vector2(81,47),new Vector2(85,53));
            Line(p,new Color(1,.84f,.56f,.35f+glow*.5f),1.1f,new Vector2(17,6),new Vector2(86,6),new Vector2(95,14));
            DrawGear(p,glow);
            foreach(float x in new[]{13f,91f}) {
                Circle(p,new Vector2(x,35),3,new Color(.12f,.09f,.06f));
                Circle(p,new Vector2(x-.5f,34.4f),1.6f,new Color(.67f,.53f,.29f));
            }
        }
        private void DrawGear(Painter2D p,float glow)
        {
            Vector2 center=new Vector2(52,29);
            var outline=new Vector2[32];var inset=new Vector2[32];
            for(int i=0;i<32;i++) {
                float a=(i*11.25f+motion.GearAngle)*Mathf.Deg2Rad;
                float r=(i%4==0 || i%4==3)?16:12.5f;
                var axis=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                outline[i]=center+axis*r;inset[i]=center+axis*(r-1.6f);
            }
            Circle(p,center+new Vector2(0,2),18,new Color(.08f,.11f,.12f));
            Polygon(p,outline,new Color(.73f,.52f,.25f),4);
            Bevel(p,outline,inset,new Color(1,.89f,.61f),new Color(.32f,.21f,.11f));
            Polygon(p,inset,Color.Lerp(new Color(.66f,.47f,.24f),new Color(.95f,.75f,.39f),glow*.7f),5);
            Circle(p,center,7.5f,new Color(.15f,.13f,.11f),6);
            Circle(p,center+new Vector2(0,.8f),5.3f,new Color(.32f,.37f,.37f),7);
            Line(p,new Color(1,.88f,.57f,.65f),1,new Vector2(47,23),new Vector2(51,21),new Vector2(56,23));
        }
    }
}

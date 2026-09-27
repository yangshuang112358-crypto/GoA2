#nullable enable
using System;
using System.Collections.Generic;
using Goa2.Domain;
using UnityEngine;

namespace Goa2.Presentation.UI3D
{
    // Pointy axial coordinates; one hex radius = one Unity unit. No physics or rules here.
    public static class Board3DGeometry
    {
        public static Vector3 World(Hex hex, float height = 0) =>
            new Vector3(Mathf.Sqrt(3) * (hex.X + hex.Y * .5f), height, -1.5f * hex.Y);

        public static Hex HexAt(Vector3 world)
        {
            float r = -world.z / 1.5f, q = world.x / Mathf.Sqrt(3) - r * .5f, s = -q - r;
            int x = Mathf.RoundToInt(q), y = Mathf.RoundToInt(r), z = Mathf.RoundToInt(s);
            float dx = Mathf.Abs(x - q), dy = Mathf.Abs(y - r), dz = Mathf.Abs(z - s);
            if (dx > dy && dx > dz) x = -y - z;
            else if (dy > dz) y = -x - z;
            return new Hex(x, y);
        }

        public static int WrapStep(int step) => (step % 12 + 12) % 12;
        public static Quaternion Rotation(int step) => Quaternion.Euler(55, WrapStep(step) * 30, 0);

        public static Mesh Prism(int sides, float phase)
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            { int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); indices.Add(n); indices.Add(n+1); indices.Add(n+2); }
            for (int i = 0; i < sides; i++)
            {
                float a = (phase + 360f * i / sides) * Mathf.Deg2Rad;
                float b = (phase + 360f * (i+1) / sides) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var q = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                Triangle(Vector3.up, q + Vector3.up, p + Vector3.up);
                Triangle(p, q + Vector3.up, q); Triangle(p, p + Vector3.up, q + Vector3.up);
                Triangle(Vector3.zero, p, q);
            }
            var mesh = new Mesh { name = "UI3D procedural prism" };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        public static Mesh Ring(int sides, float inner)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = (-30 + 360f*i/sides)*Mathf.Deg2Rad;
                vertices.Add(new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));
                vertices.Add(vertices[vertices.Count-1]*inner);
            }
            for (int i = 0; i < sides; i++)
            {
                int a=i*2,b=((i+1)%sides)*2;
                triangles.AddRange(new[] {a,b+1,b,a,a+1,b+1});
            }
            var mesh=new Mesh {name="UI3D target outline"}; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }

    // View preferences only. Not part of saves, commands, authentication or GameState.
    public sealed class Board3DViewport
    {
        public bool Enabled = !Array.Exists(Environment.GetCommandLineArgs(), a => a == "-goa2d");
        public int Step;
        public float Yaw { get; private set; }
        private float startYaw,targetYaw,elapsed;
        public bool Rotating => elapsed < .24f && Mathf.Abs(Yaw-targetYaw) > .001f;
        public void Rotate(int direction)
        {
            Step=Board3DGeometry.WrapStep(Step+direction);
            startYaw=Yaw;targetYaw+=direction*30;elapsed=0;
        }
        public bool Advance(float seconds)
        {
            bool changed=false;
            if(followTarget.HasValue)
            {
                float followT=1-Mathf.Exp(-12*Mathf.Max(0,seconds));
                var next=Vector3.Lerp(Focus,followTarget.Value,followT);
                if(Vector3.Distance(next,followTarget.Value)<.001f) next=followTarget.Value;
                changed=next!=Focus;Focus=next;
                if(followZoom.HasValue) {
                    float zoom=Mathf.Lerp(Zoom,followZoom.Value,followT);
                    if(Mathf.Abs(zoom-followZoom.Value)<.001f) {zoom=followZoom.Value;followZoom=null;}
                    changed|=zoom!=Zoom;Zoom=zoom;
                }
            }
            if(!Rotating) return changed;
            elapsed=Mathf.Min(.24f,elapsed+Mathf.Max(0,seconds));
            float t=elapsed/.24f;Yaw=Mathf.Lerp(startYaw,targetYaw,t*t*(3-2*t));
            return true;
        }
        private Vector3? followTarget;
        private float? followZoom;
        public void Follow(Vector3 target,float? zoom=null) {followTarget=target;if(zoom.HasValue) followZoom=zoom;}
        public void StopFollowing() {followTarget=null;followZoom=null;}
        public void ManualZoom() {followZoom=null;}
        public float Zoom = 1;
        public Vector3 Focus;
        public bool Initialized;
    }
}

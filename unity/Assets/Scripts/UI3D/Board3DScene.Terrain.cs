using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
namespace Goa2.Presentation.UI3D {
 public sealed partial class Board3DScene {
  private readonly Dictionary<string,Material> terrainMaterials=new Dictionary<string,Material>();
  private Material TerrainMaterial(string type,Color color) {
   string key=type+color.ToString();if(terrainMaterials.TryGetValue(key,out var result))return result;
   result=Own(new Material(Resources.Load<Shader>("UI3D/Terrain")));result.color=color;
   result.SetFloat("_Grass",type=="grass"?1:0);result.SetFloat("_Rock",type=="rock"?1:0);
   terrainMaterials.Add(key,result);return result;
  }
  private static Mesh Faces(List<Vector3> v,string name) {
   var mesh=new Mesh{name=name};mesh.SetVertices(v);var t=new int[v.Count];for(int i=0;i<t.Length;i++)t[i]=i;
   mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
  }
  private static void Tri(List<Vector3> v,Vector3 a,Vector3 b,Vector3 c){v.Add(a);v.Add(b);v.Add(c);}
  // A shared top edge and no interior walls make neighboring obstacle cells one rock mass.
  private void BuildConnectedRocks() {
   var obstacles=cells.Values.Where(c=>c.Obstacle).ToList();var vertices=new List<Vector3>();
   var centers=obstacles.Select(c=>Board3DGeometry.World(c.Position)).ToList();
   var symmetryCenter=(Board3DGeometry.World(new Hex(0,0))+Board3DGeometry.World(new Hex(0,1)))*.5f;
   Vector3 Bottom(Vector3 top,float inset,float y){var near=centers.Where(c=>Vector2.Distance(new Vector2(c.x,c.z),new Vector2(top.x,top.z))<1.12f).ToList();var average=near.Aggregate(Vector3.zero,(a,b)=>a+b)/near.Count;var p=Vector3.Lerp(top,average,inset);p.y=y;return p;}
   foreach(var cell in obstacles){
    var center=Board3DGeometry.World(cell.Position);var top=new Vector3[6];
    bool central=cell.Position==new Hex(0,0)||cell.Position==new Hex(0,1);
    for(int i=0;i<6;i++){
     float a=(-30+i*60)*Mathf.Deg2Rad;top[i]=center+new Vector3(Mathf.Cos(a),WallHeight,Mathf.Sin(a));
     if(!central){var d=top[i]-symmetryCenter;d.y=0;float noise=Mathf.Cos(d.x*3.7f)+Mathf.Cos(d.z*2.3f);
      // The same world vertex gives the same perturbation on adjacent rocks;
      // cosine and radial offset preserve exact central symmetry.
      top[i]+=d.normalized*(noise*.035f);top[i].y+=.08f+noise*.055f;}
    }
    var baseTile=Add(Own(Board3DGeometry.Prism(6,-30)),center,new Vector3(.995f,.09f,.995f),ColorOf("#686659"),"rock hex foundation");
    baseTile.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial("rock",ColorOf("#686659"));
    // Canonical mirrored coordinates preserve exact 180-degree shape symmetry about (0, .5).
    var h=cell.Position;int x=h.X,y=h.Y;if(x<0 || x==0 && y<1){x=-x;y=1-y;}
    float peak=central?WallHeight:WallHeight+.12f+((x*31+y*17)&7)*.025f;
    for(int i=0;i<6;i++){
     int j=(i+1)%6;Tri(vertices,center+Vector3.up*peak,top[j],top[i]);
     var midpoint=(top[i]+top[j])*.5f;var other=Board3DGeometry.HexAt(center+(midpoint-center)*1.1f);
     if(cells.TryGetValue(other,out var neighbor)&&neighbor.Obstacle)continue;
     var lowerA=Bottom(top[i],.28f,0);var lowerB=Bottom(top[j],.28f,0);
     var bevelA=Bottom(top[i],.06f,WallHeight*.70f);var bevelB=Bottom(top[j],.06f,WallHeight*.70f);
     Tri(vertices,lowerA,bevelA,bevelB);Tri(vertices,lowerA,bevelB,lowerB);
     Tri(vertices,bevelA,top[i],top[j]);Tri(vertices,bevelA,top[j],bevelB);
    }
   }
   var go=Add(Own(Faces(vertices,"connected symmetric rocks")),Vector3.zero,Vector3.one,ColorOf("#817D70"),"connected rocks");
   go.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial("rock",ColorOf("#817D70"));
   var tray=Add(Own(Board3DGeometry.Prism(96,0)),symmetryCenter+Vector3.up*WallHeight,new Vector3(1.12f,.09f,1.12f),ColorOf("#8B877B"),"central circular tray");
   tray.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial("rock",ColorOf("#8B877B"));
   var trayRim=new List<Vector3>();
   for(int i=0;i<96;i++){
    float a=i*Mathf.PI/48,b=(i+1)*Mathf.PI/48;
    Vector3 Point(float angle,float r,float h)=>symmetryCenter+new Vector3(Mathf.Cos(angle)*r,WallHeight+h,Mathf.Sin(angle)*r);
    foreach(float radius in new[]{1.12f,1.045f}){
     var p=Point(a,radius,.09f);var q=Point(b,radius,.09f);var r=Point(a,radius,.23f);var s=Point(b,radius,.23f);
     if(radius>1.1f){Tri(trayRim,p,r,s);Tri(trayRim,p,s,q);}else{Tri(trayRim,p,s,r);Tri(trayRim,p,q,s);}
    }
    Tri(trayRim,Point(a,1.045f,.23f),Point(b,1.12f,.23f),Point(a,1.12f,.23f));
    Tri(trayRim,Point(a,1.045f,.23f),Point(b,1.045f,.23f),Point(b,1.12f,.23f));
   }
   Add(Own(Faces(trayRim,"vertical circular tray rim")),Vector3.zero,Vector3.one,ColorOf("#B09B6A"),"central tray upright rim");
   var origin=symmetryCenter+Vector3.up*(WallHeight+.094f);
   var engraving=Add(Own(Board3DGeometry.Ring(64,.94f)),origin,new Vector3(.81f,1,.39f),ColorOf("#39332D"),"coin platform engraving");engraving.transform.localRotation=Quaternion.Euler(0,60,0);
   var cuts=new List<Vector3>();var rotation=Quaternion.Euler(0,60,0);
   for(int sign=-1;sign<=1;sign+=2){var tip=origin+rotation*new Vector3(sign*.67f,.001f,0);var a=rotation*new Vector3(.10f,0,0);var b=rotation*new Vector3(0,0,.075f);Tri(cuts,tip-a,tip+b,tip+a);Tri(cuts,tip-a,tip+a,tip-b);}
   Add(Own(Faces(cuts,"opposed coin glyph cuts")),Vector3.zero,Vector3.one,ColorOf("#39332D"),"coin platform glyph");
  }
  private static Mesh Grass(int seed) {
   var rng=new System.Random(seed);var v=new List<Vector3>();
   for(int i=0;i<112;i++){
    float a=(float)rng.NextDouble()*Mathf.PI*2,r=Mathf.Sqrt((float)rng.NextDouble())*.77f;
    var at=new Vector3(Mathf.Cos(a)*r,.006f,Mathf.Sin(a)*r);
    float h=.065f+(float)rng.NextDouble()*.16f,w=.016f+(float)rng.NextDouble()*.021f;
    var across=new Vector3(Mathf.Cos(a+1),0,Mathf.Sin(a+1))*w;
    var tip=at+new Vector3(.025f,h,.015f);Tri(v,at-across,tip,at+across);Tri(v,at+across,tip,at-across);
   }
   return Faces(v,"short grass blades");
  }
  private void BuildTerrain(CellDefinition cell,Mesh hex) {
   int seed=unchecked(cell.Position.X*73856093^cell.Position.Y*19349663)&int.MaxValue;
   bool grass=cell.Region=="topGrass"||cell.Region=="bottomGrass";
   Color color=ColorOf(grass?"#586440":cell.Region=="redNear"?"#D2A29E":cell.Region=="blueNear"?"#9EBACF":cell.Region=="redFountain"?"#82414E":cell.Region=="blueFountain"?"#365C83":cell.Lane?"#9A7955":"#80684D");
   var tile=Add(hex,Board3DGeometry.World(cell.Position,-.14f),new Vector3(.985f,.14f,.985f),color,"hex "+cell.Position);
   tile.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial(grass?"grass":"earth",color);
   if(!cell.Obstacle && grass){
    var blades=Add(Own(Grass(seed)),Board3DGeometry.World(cell.Position),Vector3.one,ColorOf("#7D9550"),"short grass");
    blades.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial("grass",ColorOf("#7D9550"));
   }
   if(cell.Spawn.EndsWith("Spawn",StringComparison.Ordinal))BuildSpawnRune(cell);
  }
  private void BuildSpawnRune(CellDefinition cell) {
   var backing=Add(Own(Board3DGeometry.Prism(48,0)),Board3DGeometry.World(cell.Position,.004f),new Vector3(.745f,.006f,.745f),ColorOf("#37343C"),"spawn rune backing");
   var v=new List<Vector3>();
   void Stroke(Vector2 a,Vector2 b,float width=.019f){
    var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;
    Vector3 P(Vector2 p)=>new Vector3(p.x,.018f,p.y);
    Tri(v,P(a-n),P(b+n),P(b-n));Tri(v,P(a-n),P(a+n),P(b+n));
   }
   void Path(params Vector2[] points){for(int i=1;i<points.Length;i++)Stroke(points[i-1],points[i],.035f);}
   foreach(float radius in new[]{.70f,.60f})for(int i=0;i<64;i++){
    float a=i*Mathf.PI/32,b=(i+1)*Mathf.PI/32;Stroke(new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius);
   }
   for(int i=0;i<12;i++){
    float a=i*Mathf.PI/6;var d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var n=new Vector2(-d.y,d.x);
    Stroke(d*.62f,d*.68f,.025f);Stroke(d*.65f,d*.65f+n*.028f,.022f);
   }
   if(cell.Spawn.Contains("Hero")) {
    Path(new Vector2(-.30f,-.24f),new Vector2(.30f,-.24f),new Vector2(.34f,.17f),new Vector2(.14f,.02f),new Vector2(0,.34f),new Vector2(-.14f,.02f),new Vector2(-.34f,.17f),new Vector2(-.30f,-.24f));
    Path(new Vector2(-.26f,-.12f),new Vector2(.26f,-.12f));
   } else if(cell.Spawn.Contains("Heavy")) {
    Path(new Vector2(0,.34f),new Vector2(.28f,.19f),new Vector2(.24f,-.17f),new Vector2(0,-.34f),new Vector2(-.24f,-.17f),new Vector2(-.28f,.19f),new Vector2(0,.34f));
    Path(new Vector2(-.15f,.04f),new Vector2(.15f,.04f));Path(new Vector2(0,.21f),new Vector2(0,-.21f));
   } else if(cell.Spawn.Contains("Ranged")) {
    Path(new Vector2(-.23f,-.29f),new Vector2(-.06f,0),new Vector2(-.23f,.29f),new Vector2(-.23f,-.29f));
    Path(new Vector2(-.32f,0),new Vector2(.32f,0),new Vector2(.16f,.14f));Path(new Vector2(.32f,0),new Vector2(.16f,-.14f));
   } else {
    foreach(int side in new[]{-1,1}){
     Path(new Vector2(-.24f*side,-.30f),new Vector2(.22f*side,.26f),new Vector2(.12f*side,.28f));
     Path(new Vector2(-.25f*side,-.06f),new Vector2(-.06f*side,-.22f));
    }
   }
   var rune=Add(Own(Faces(v,"spawn rune "+cell.Spawn)),Board3DGeometry.World(cell.Position),Vector3.one,ColorOf(cell.Spawn.StartsWith("blue")?"#87D9FF":"#FFA4A0"),"spawn rune "+cell.Spawn);
   var mat=Own(new Material(Resources.Load<Shader>("UI3D/Crystal")));mat.color=rune.GetComponent<MeshRenderer>().sharedMaterial.color;mat.SetFloat("_Rune",1);rune.GetComponent<MeshRenderer>().sharedMaterial=mat;
  }
 }
}

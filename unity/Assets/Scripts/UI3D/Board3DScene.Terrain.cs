using System;
using System.Collections.Generic;
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
  private static Mesh Rock(int seed) {
   var rng=new System.Random(seed);var v=new List<Vector3>();var rings=new Vector3[4,9];
   for(int j=0;j<4;j++)for(int i=0;i<9;i++){
    float a=(i*40+seed%17)*Mathf.Deg2Rad;
    float r=(j==0?.78f:j==1?.83f:j==2?.66f:.44f)*(float)(.88+rng.NextDouble()*.12);
    float y=j==0?0:j==1?.19f:j==2?.63f:.78f+(float)rng.NextDouble()*.22f;
    float lean=j<2?0:(seed%7-3)*.027f;
    rings[j,i]=new Vector3(Mathf.Cos(a)*r+lean,y+(j==1||j==2?(float)rng.NextDouble()*.16f:0),Mathf.Sin(a)*r-lean*.6f);
   }
   for(int j=0;j<3;j++)for(int i=0;i<9;i++){int n=(i+1)%9;Tri(v,rings[j,i],rings[j+1,n],rings[j,n]);Tri(v,rings[j,i],rings[j+1,i],rings[j+1,n]);}
   for(int i=0;i<9;i++)Tri(v,Vector3.up,rings[3,(i+1)%9],rings[3,i]);
   return Faces(v,"layered fractured rock");
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
   Color color=ColorOf(grass?"#586440":cell.Lane?"#9A7955":"#80684D");
   var tile=Add(hex,Board3DGeometry.World(cell.Position,-.14f),new Vector3(.985f,.14f,.985f),color,"hex "+cell.Position);
   tile.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial(grass?"grass":"earth",color);
   if(cell.Obstacle){
    var rock=Add(Own(Rock(seed)),Board3DGeometry.World(cell.Position),new Vector3(1,WallHeight,1),ColorOf("#817D70"),"rock "+cell.Position);
    rock.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial("rock",ColorOf("#817D70"));
    // Low chips soften the silhouette without reaching neighboring cells.
    var chip=Add(Own(Rock(seed+17)),Board3DGeometry.World(cell.Position)+new Vector3(.5f,0,-.23f),new Vector3(.28f,.22f,.28f),ColorOf("#716C60"),"rock chip");
    chip.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial("rock",ColorOf("#716C60"));
   } else if(grass){
    var blades=Add(Own(Grass(seed)),Board3DGeometry.World(cell.Position),Vector3.one,ColorOf("#7D9550"),"short grass");
    blades.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial("grass",ColorOf("#7D9550"));
   }
   if(cell.Spawn.EndsWith("Spawn",StringComparison.Ordinal))BuildSpawnRune(cell);
  }
  private void BuildSpawnRune(CellDefinition cell) {
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
   var rune=Add(Own(Faces(v,"spawn rune "+cell.Spawn)),Board3DGeometry.World(cell.Position),Vector3.one,ColorOf(cell.Spawn.StartsWith("blue")?"#56BFFF":"#FA6970"),"spawn rune "+cell.Spawn);
   var mat=Own(new Material(Resources.Load<Shader>("UI3D/Crystal")));mat.color=rune.GetComponent<MeshRenderer>().sharedMaterial.color;mat.SetFloat("_Rune",1);rune.GetComponent<MeshRenderer>().sharedMaterial=mat;
  }
 }
}

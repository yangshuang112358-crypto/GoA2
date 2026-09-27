#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
namespace Goa2.Presentation.UI3D
{
 public sealed partial class Board3DScene
 {
  private readonly List<(Transform transform,Vector3 origin,float phase)> crystals=new List<(Transform,Vector3,float)>();
  private readonly List<(Transform transform,BattlePresentationState.CrownFlight flight,Vector3 to)> crowns=new List<(Transform,BattlePresentationState.CrownFlight,Vector3)>();
  private readonly List<(Transform transform,float until)> pendingCrystals=new List<(Transform,float)>();
  private readonly List<(Transform transform,BattlePresentationState.Shatter effect,Vector3 origin,Vector3 velocity)> fragments=new List<(Transform,BattlePresentationState.Shatter,Vector3,Vector3)>();
  private Transform? decisionCoin;
  private Vector3 coinOrigin;
  private readonly List<Vector3> framingPoints=new List<Vector3>();
  private BattlePresentationState presentation=null!;
  private static Mesh Gem()
  {
   var vertices=new List<Vector3>{new Vector3(0,1,0),new Vector3(0,-1,0)};var indices=new List<int>();
   for(int i=0;i<6;i++){float a=i*Mathf.PI/3;vertices.Add(new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));}
   for(int i=0;i<6;i++){int a=i+2,b=(i+1)%6+2;indices.AddRange(new[]{0,b,a,1,a,b});}
   var mesh=new Mesh{name="faceted crystal"};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();return mesh;
  }
  public static Vector3 CrystalPosition(ContentCatalog catalog,Team team,int index,int count)
  {
   var center=BattlePresentationState.Center(catalog);var fountain=BattlePresentationState.Center(catalog,team==Team.Blue ? "blueFountain" : "redFountain");
   float angle=Mathf.Atan2(fountain.z-center.z,fountain.x-center.x)+.43f-(count<=1 ? .43f : .86f*index/(count-1));
   float radius=Vector3.Distance(fountain,center)+5;
   return center+new Vector3(Mathf.Cos(angle)*radius,1.2f,Mathf.Sin(angle)*radius);
  }
  private Transform Crown(Mesh disk,Vector3 at)
  {
   var coin=Add(disk,at,new Vector3(.55f,.12f,.55f),ColorOf("#E4B95C"),"crown coin").transform;
   // Raised crown silhouette: gold base and three pointed prongs on its visible face.
   var crown=new Mesh{name="crown emblem"};var v=new[]{new Vector3(-.65f,1.08f,-.4f),new Vector3(.65f,1.08f,-.4f),new Vector3(.75f,1.08f,.6f),new Vector3(.26f,1.08f,.15f),new Vector3(0,1.08f,.8f),new Vector3(-.26f,1.08f,.15f),new Vector3(-.75f,1.08f,.6f)};
   crown.vertices=v;crown.triangles=new[]{0,2,1,0,3,2,0,4,3,0,5,4,0,6,5};crown.RecalculateNormals();Own(crown);
   var emblem=Add(crown,Vector3.zero,Vector3.one,ColorOf("#67421D"),"crown emblem").transform;emblem.SetParent(coin,false);return coin;
  }
  private void BuildWorldHud(ContentCatalog catalog,GameView view)
  {
   presentation=state.Presentation;presentation.Observe(catalog,view,Time.realtimeSinceStartup);var gem=Own(Gem());var disk=Own(Board3DGeometry.Prism(48,0));
   foreach(var team in new[]{Team.Blue,Team.Red}) {
    int capacity=team==Team.Blue ? presentation.BlueCapacity : presentation.RedCapacity;
    if(capacity<=0)capacity=catalog.Rules.StartingCrystalLife;
    int remaining=Mathf.Max(0,team==Team.Blue ? view.BlueCrystal : view.RedCrystal);
    for(int i=0;i<capacity;i++)if(i>=capacity-remaining) {
     var at=CrystalPosition(catalog,team,i,capacity);framingPoints.Add(at);var tr=Add(gem,at,new Vector3(.4f,.75f,.4f),ColorOf(team==Team.Blue ? "#59BAFF" : "#FF626B"),"crystal life "+team+" "+i).transform;
     crystals.Add((tr,at,i*1.37f+(team==Team.Blue ? 0 : 2.5f)));
    }
    framingPoints.Add(CrystalPosition(catalog,team,0,capacity));framingPoints.Add(CrystalPosition(catalog,team,Mathf.Max(0,capacity-1),capacity));
    var crystalBase=CrystalPosition(catalog,team,capacity/2,capacity);crystalBase.y=.1f;
    Add(gem,crystalBase,new Vector3(1.1f,1.7f,1.1f),ColorOf(team==Team.Blue ? "#3D79CF" : "#B94460"),"team crystal "+team);
    int marks=team==Team.Blue ? view.BlueMarks : view.RedMarks;
    for(int i=0;i<marks;i++) {
     var at=crystalBase+new Vector3((i-(view.VictoryMarksRequired-1)*.5f)*1.3f,0,team==Team.Blue ? 2 : -2);
     var flight=presentation.Crowns.LastOrDefault(f=>f.Team==team && f.Index==i && Time.realtimeSinceStartup-f.Started<2.4f);
     var coin=Crown(disk,at);if(flight!=null)crowns.Add((coin,flight,at));
    }
   }
   foreach(var effect in presentation.Shards) {
    int capacity=effect.Team==Team.Blue ? presentation.BlueCapacity : presentation.RedCapacity;
    var at=CrystalPosition(catalog,effect.Team,effect.Index,capacity);
    var intact=Add(gem,at,new Vector3(.4f,.75f,.4f),ColorOf(effect.Team==Team.Blue ? "#59BAFF" : "#FF626B"),"pending shatter").transform;
    pendingCrystals.Add((intact,effect.Started));
    for(int i=0;i<5;i++) {
     float a=i*Mathf.PI*2/5;var tr=Add(gem,at,Vector3.one*.18f,ColorOf(effect.Team==Team.Blue ? "#82D0FF" : "#FF8196"),"crystal shard").transform;
     fragments.Add((tr,effect,at,new Vector3(Mathf.Cos(a)*2,2+i*.23f,Mathf.Sin(a)*2)));
    }
   }
   coinOrigin=BattlePresentationState.Center(catalog)+Vector3.up*3.2f;
   var coinMesh=Own(Board3DGeometry.Prism(48,0));var cv=coinMesh.vertices;for(int i=0;i<cv.Length;i++)cv[i].y-=.5f;coinMesh.vertices=cv;coinMesh.RecalculateBounds();
   decisionCoin=Add(coinMesh,coinOrigin,new Vector3(1.05f,.18f,1.05f),ColorOf("#DCAE55"),"decision coin").transform;
   foreach(var side in new[]{-1,1}) {
    var face=Add(Own(Board3DGeometry.Ring(48,.83f)),Vector3.zero,Vector3.one*.9f,ColorOf("#FFE6A0"),"coin rim").transform;face.SetParent(decisionCoin,false);face.localPosition=new Vector3(0,side*.55f,0);if(side<0)face.localRotation=Quaternion.Euler(180,0,0);
    var jewel=Add(gem,Vector3.zero,new Vector3(.5f,.35f,.5f),ColorOf(side==1 ? "#D9304A" : "#367FE7"),"hexagonal jewel").transform;jewel.SetParent(decisionCoin,false);jewel.localPosition=new Vector3(0,side*.68f,0);
   }
  }
  private void AnimateWorldHud()
  {
   float now=Time.realtimeSinceStartup;
   foreach(var c in crystals)c.transform.localPosition=c.origin+Vector3.up*(.16f*Mathf.Sin(now*(.7f+(c.phase%1)*.6f)+c.phase));
   foreach(var c in crowns) {float t=Mathf.Clamp01((now-c.flight.Started)/2.4f),s=t*t*(3-2*t);c.transform.localPosition=Vector3.Lerp(c.flight.From,c.to,s)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*3);c.transform.localRotation=Quaternion.Euler(0,360*t,0);}
   foreach(var p in pendingCrystals)p.transform.gameObject.SetActive(now<p.until);
   foreach(var f in fragments) {float t=now-f.effect.Started;f.transform.gameObject.SetActive(t>=0 && t<1.5f);if(t>=0){f.transform.localPosition=f.origin+f.velocity*t-Vector3.up*3*t*t;f.transform.localScale=Vector3.one*.18f*Mathf.Clamp01(1-t/1.5f);}}
   if(decisionCoin!=null) {float t=Mathf.Clamp01((now-presentation.CoinStarted)/2.4f);float turn=presentation.CoinTo==Team.Blue ? 180 : 0;
    decisionCoin.localPosition=coinOrigin+Vector3.up*(Mathf.Sin(t*Mathf.PI)*4+.08f*Mathf.Sin(now));
    float from=presentation.CoinFrom==Team.Blue ? 180 : 0;float ease=t*t*(3-2*t);
    decisionCoin.localRotation=Quaternion.Euler(18,0,from+(turn-from+720)*ease);}
  }
 }
}

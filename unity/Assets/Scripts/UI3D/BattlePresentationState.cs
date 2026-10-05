#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
namespace Goa2.Presentation.UI3D
{
 public sealed class BattlePresentationState
 {
  public sealed class CrownFlight {public Team Team;public Vector3 From;public float Started;public int Index;}
  public sealed class Shatter {public Team Team;public int Index;public float Started;}
  public readonly List<CrownFlight> Crowns=new List<CrownFlight>();
  public readonly List<Shatter> Shards=new List<Shatter>();
  public readonly Dictionary<string,MinionPoseMotion> MinionMotions=new Dictionary<string,MinionPoseMotion>();
  public readonly Dictionary<int,LevelPreview.Motion> Levels=new Dictionary<int,LevelPreview.Motion>();
  public readonly CombatPresentationTimeline Combat=new CombatPresentationTimeline();
  public int BlueCapacity,RedCapacity;
  public float CoinStarted=-100,DeathUntil=-100;
  public float RockFormationStarted=-100;
  public bool CoinOpening;
  public bool HideCoin;
  public float CoinDuration => CoinOpening ? 2.4f : .32f;
  public Team CoinFrom, CoinTo;
  public Vector3 DeathFocus;
  private string match="";
  private bool initialized;
  private long sequence,lastRevision;
  private static float Travel(Vector3? from,Vector3 to)=>from.HasValue ? 1.8f+Vector3.Distance(from.Value,to)/14 : 0;
  public static Vector3 Center(ContentCatalog catalog,string region="") {
   var points=catalog.Cells.Where(c=>region=="" || c.Region==region).Select(c=>Board3DGeometry.World(c.Position)).ToList();
   return points.Count==0 ? Vector3.zero : new Vector3((points.Min(p=>p.x)+points.Max(p=>p.x))*.5f,0,(points.Min(p=>p.z)+points.Max(p=>p.z))*.5f);
  }
  public void Observe(ContentCatalog catalog,GameView view,float now,Vector3? cameraPosition=null)
  {
   bool fresh=!initialized || match!=view.MatchId || view.Revision<lastRevision;
   Combat.Observe(view,now);
   lastRevision=view.Revision;
   if(fresh) {initialized=true;match=view.MatchId;sequence=0;Crowns.Clear();Shards.Clear();DeathUntil=-100;CoinFrom=CoinTo=view.DecisionCoin;CoinOpening=view.Revision==0;CoinStarted=view.Revision==0 ? now+Travel(cameraPosition,Center(catalog)) : -100;}
   if(fresh){Levels.Clear();MinionMotions.Clear();RockFormationStarted=-100;HideCoin=view.Opening!=null && !view.Opening.OpeningComplete;}
   foreach(var player in view.Players){
    if(!Levels.TryGetValue(player.Seat,out var motion))Levels[player.Seat]=motion=new LevelPreview.Motion();
    if(view.RoundEndStage!="upgrades" || !view.UpgradingSeats.Contains(player.Seat))motion.Observe(LevelPreview.Target(player.Level,Combat.VisibleGold(player.Seat,player.Gold,now))-player.Level,now);
   }
   BlueCapacity=RedCapacity=catalog.Rules.StartingCrystalLife;
   int blue=BlueCapacity,red=RedCapacity;Hex? removed=null;
   int blueMark=0,redMark=0;
   foreach(var e in view.Events.OrderBy(e=>e.Sequence)) {
    if(e.Kind=="DebugCrystalSet") {var parts=e.Detail.Split(':');if(parts.Length==2 && int.TryParse(parts[1],out int n)) {if(parts[0]=="Blue")blue=BlueCapacity=n;else red=RedCapacity=n;}}
    if(e.Kind=="MinionRemoved" || e.Kind=="MinionDefeated")removed=e.From;
    bool added=!fresh && e.Sequence>sequence;
    if(added && e.Kind=="CoinTossStarted" && e.Detail.StartsWith("opening:"))RockFormationStarted=now+StageBannerPolicy.Duration;
    if(e.Kind=="CrystalDamaged" && e.Seat.HasValue && int.TryParse(e.Detail,out int damage)) {
     var team=view.Players.Single(p=>p.Seat==e.Seat).Team;int capacity=team==Team.Blue ? BlueCapacity : RedCapacity;int old=team==Team.Blue ? blue : red;
     if(added)for(int i=0;i<damage && old-i>0;i++)Shards.Add(new Shatter{Team=team,Index=capacity-old+i,Started=Combat.HeroImpact(e.Seat.Value,now)+Travel(cameraPosition,Center(catalog,team==Team.Blue ? "blueFountain" : "redFountain"))+i*.12f});
     if(team==Team.Blue)blue-=damage;else red-=damage;
    }
    if(added && e.Kind=="HeroDefeated" && e.Seat.HasValue) {
     var team=view.Players.Single(p=>p.Seat==e.Seat).Team;
     var spawn=catalog.Cells.Where(c=>c.Spawn==(team==Team.Blue ? "blueHeroSpawn" : "redHeroSpawn")).ToList();
     DeathFocus=spawn.Count>0 ? spawn.Select(c=>Board3DGeometry.World(c.Position)).Aggregate(Vector3.zero,(a,b)=>a+b)/spawn.Count : Center(catalog,team==Team.Blue ? "blueFountain" : "redFountain");DeathUntil=Mathf.Max(now,Combat.BusyUntil)+Travel(cameraPosition,DeathFocus)+2.5f;
    }
    if(e.Kind=="FrontlineMarkGained") {
     Team team=e.Detail=="Blue" ? Team.Blue : Team.Red;int index=team==Team.Blue ? blueMark++ : redMark++;
     if(added)Crowns.Add(new CrownFlight{Team=team,Index=index,From=removed.HasValue ? Board3DGeometry.World(removed.Value,1) : Center(catalog,view.CombatRegion),Started=now});
    }
   }
   if(!fresh && CoinTo!=view.DecisionCoin) {CoinOpening=false;CoinFrom=CoinTo;CoinTo=view.DecisionCoin;CoinStarted=now+Travel(cameraPosition,Center(catalog));}
   if(view.Opening!=null && (!view.Opening.OpeningComplete || fresh || HideCoin)){CoinOpening=false;CoinFrom=CoinTo=view.DecisionCoin;CoinStarted=-100;}
   sequence=view.Events.Count==0 ? sequence : System.Math.Max(sequence,view.Events.Max(e=>e.Sequence));
   BlueCapacity=Mathf.Max(BlueCapacity,view.BlueCrystal);RedCapacity=Mathf.Max(RedCapacity,view.RedCrystal);
   Crowns.RemoveAll(c=>now-c.Started>4);Shards.RemoveAll(c=>now-c.Started>2);
  }
 }
}

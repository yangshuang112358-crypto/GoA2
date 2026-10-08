#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
namespace Goa2.Presentation.UI3D
{
    // Consumes public events only. Cached positions are visual snapshots, never legal targets.
    public sealed class CombatPresentationTimeline
    {
        // Release-frame world geometry survives scene rebuilds with its owning shot.
        public sealed class ArrowLaunch
        {
            public Vector3 Tail,Direction;
            public float Length;
        }
        public sealed class Shot
        {
            public long Id;public float Start,PrepareStarted;public UnitState Source=null!,Target=null!;
            public List<UnitState> Support=new List<UnitState>(),Guard=new List<UnitState>();
            public readonly Dictionary<string,ArrowLaunch> Arrows=new Dictionary<string,ArrowLaunch>();
            public bool Defeated;public string CardId="";
            public float Release=>Start+.12f;
            public float Impact=>Start+.62f;
            public float End=>Start+1.2f;
        }
        public sealed class Reward
        {
            public long Id;public int Seat,Amount;public float Start;public Vector3 Origin;
            public float Arrival=>Start+3.8f;
        }
        private readonly Dictionary<string,UnitState> units=new Dictionary<string,UnitState>();
        private readonly Dictionary<int,Shot> waiting=new Dictionary<int,Shot>();
        public readonly List<Shot> Shots=new List<Shot>();
        public readonly List<Reward> Rewards=new List<Reward>();
        private string match="";private long sequence,revision;private int round;
        private bool ready;
        public int Generation {get;private set;}
        public float BusyUntil=>Shots.Select(s=>s.End).DefaultIfEmpty(-100).Max();
        public void Reset(){ready=false;}
        public static bool IsPendingAttack(GameView view)
        {
            var attack=view.Attack;
            if(attack==null)return false;
            // Attack is retained by the core during attack-after movement and defense
            // responses. A defense-before purple choice, conversely, is still pending
            // even though Pending.Kind is no longer "defense".
            long declared=view.Events.Where(e=>e.Kind=="AttackCalculated" && e.AttackValues!=null &&
                e.AttackValues.AttackerSeat==attack.AttackerSeat && e.AttackValues.TargetUnitId==attack.TargetUnitId &&
                e.AttackValues.SourceCardId==attack.SourceCardId).Select(e=>e.Sequence).DefaultIfEmpty(-1).Max();
            if(declared<0)
                return view.Pending?.Kind=="defense" && view.Pending.ChooserSeat==attack.DefenderSeat &&
                    view.Pending.UnitId==attack.TargetUnitId;
            return !view.Events.Any(e=>e.Sequence>declared &&
                (e.Kind=="DefenseResolved" && e.Seat==attack.DefenderSeat ||
                 e.Kind=="AttackResolved" && e.Seat==attack.AttackerSeat));
        }
        public int VisibleGold(int seat,int actual,float now)=>Math.Max(0,actual-Rewards.Where(r=>r.Seat==seat && now<r.Arrival).Sum(r=>r.Amount));
        public Shot? Current(float now)=>Shots.FirstOrDefault(s=>s.End>now);
        public IEnumerable<UnitState> Ghosts(GameView view,float now)=>Shots.Where(s=>s.End>now && s.Defeated && !view.Units.Any(u=>u.Id==s.Target.Id && u.Position==s.Target.Position)).Select(s=>s.Target).GroupBy(u=>u.Id).Select(g=>g.First());
        public void Observe(GameView view,float now)
        {
            bool fresh=!ready || match!=view.MatchId || view.Revision<revision;
            if(fresh)
            {
                ready=true;match=view.MatchId;sequence=view.Events.Select(e=>e.Sequence).DefaultIfEmpty(0).Max();Shots.Clear();Rewards.Clear();waiting.Clear();units.Clear();Generation++;
                foreach(var u in view.Units)units[u.Id]=Copy(u);
                RememberPending(view,now);revision=view.Revision;round=view.Round;return;
            }
            if(view.Round!=round || view.RoundEndStage=="upgrades")Rewards.Clear();
            var freshEvents=view.Events.Where(e=>e.Sequence>sequence).OrderBy(e=>e.Sequence).ToList();
            var working=units.ToDictionary(p=>p.Key,p=>Copy(p.Value));
            string command="";Vector3? defeatOrigin=null;float defeatStart=now,discardEnd=now;
            foreach(var e in freshEvents)
            {
                if(command!=e.CommandId){command=e.CommandId;defeatOrigin=null;defeatStart=now;discardEnd=now;}
                if(e.Kind=="DiscardColorShown")discardEnd+=2.4f;
                if(e.From.HasValue && e.To.HasValue && (e.Kind=="UnitMoved" || e.Kind=="UnitPlaced" || e.Kind=="UnitPushed" || e.Kind=="UnitsSwapped"))
                {
                    var moved=working.Values.FirstOrDefault(u=>u.Position==e.From.Value);
                    var swapped=e.Kind=="UnitsSwapped"?working.Values.FirstOrDefault(u=>u.Position==e.To.Value):null;
                    if(moved!=null)moved.Position=e.To.Value;if(swapped!=null)swapped.Position=e.From.Value;
                }
                if(e.Kind=="HeroRespawned" && e.Seat.HasValue && e.To.HasValue)
                {var u=view.Units.FirstOrDefault(x=>x.Seat==e.Seat);if(u!=null){working[u.Id]=Copy(u);working[u.Id].Position=e.To.Value;}}
                if(e.Kind=="AttackDeclared" && e.Seat.HasValue)
                {
                    var shot=Capture(working,e.Seat.Value,e.Detail,e.CardId??"");if(shot!=null){shot.PrepareStarted=now;waiting[e.Seat.Value]=shot;}
                }
                if(e.Kind=="AttackCalculated" && e.AttackValues!=null)
                {
                    var a=e.AttackValues;var shot=Capture(working,a.AttackerSeat,a.TargetUnitId,a.SourceCardId);if(shot!=null){shot.PrepareStarted=now;waiting[a.AttackerSeat]=shot;}
                }
                if(e.Kind=="DefenseResolved" && e.Seat.HasValue)
                {
                    var shot=waiting.Values.LastOrDefault(s=>s.Target.Seat==e.Seat);
                    if(shot!=null)
                    {
                        var latest=Capture(working,shot.Source.Seat!.Value,shot.Target.Id,shot.CardId);
                        if(latest!=null)
                        {
                            // A defense-before move may bring a new bow into support. It
                            // has not shared the original bows' defense waiting time.
                            bool newBow=latest.Support.Any(u=>u.Kind=="ranged" && !shot.Support.Any(old=>old.Id==u.Id && old.Kind=="ranged"));
                            latest.PrepareStarted=newBow?Mathf.Max(now,shot.PrepareStarted):shot.PrepareStarted;
                            shot=latest;
                        }
                        Schedule(shot,e.Sequence,Mathf.Max(now,discardEnd),e.Detail=="failure");waiting.Remove(shot.Source.Seat!.Value);
                    }
                }
                if(e.Kind=="HeroDefeated" || e.Kind=="MinionDefeated")
                {
                    string id=e.Kind=="HeroDefeated"?"hero:"+e.Seat:e.Detail;
                    var shot=waiting.Values.LastOrDefault(s=>s.Target.Id==id);
                    if(shot!=null){Schedule(shot,e.Sequence,Mathf.Max(now,discardEnd),true);waiting.Remove(shot.Source.Seat!.Value);}
                    var shown=Shots.LastOrDefault(s=>s.Target.Id==id && s.End>now);if(shown!=null)shown.Defeated=true;
                    if(e.From.HasValue){defeatOrigin=Board3DGeometry.World(e.From.Value,.75f);defeatStart=shown?.Impact??Mathf.Max(now,discardEnd);}
                    working.Remove(id);
                }
                if((e.Kind=="GoldAwarded" || e.Kind=="AssistGoldAwarded") && e.Seat.HasValue && defeatOrigin.HasValue && int.TryParse(e.Detail,out int amount) && amount>0)
                    Rewards.Add(new Reward{Id=e.Sequence,Seat=e.Seat.Value,Amount=amount,Origin=defeatOrigin.Value,Start=defeatStart});
                // A defended minion or invalidated attack must not leave a cached strike for a later unrelated defeat.
                if(e.Kind=="AttackResolved" && e.Seat.HasValue)waiting.Remove(e.Seat.Value);
            }
            sequence=Math.Max(sequence,view.Events.Select(e=>e.Sequence).DefaultIfEmpty(0).Max());revision=view.Revision;round=view.Round;
            units.Clear();foreach(var u in view.Units)units[u.Id]=Copy(u);
            if(waiting.Count==0)RememberPending(view,now);
            Shots.RemoveAll(s=>s.End<now-1);Rewards.RemoveAll(r=>r.Arrival<now);
            if(Shots.Count>24)Shots.RemoveRange(0,Shots.Count-24);if(Rewards.Count>64)Rewards.RemoveRange(0,Rewards.Count-64);
        }
        private void RememberPending(GameView view,float now)
        {
            if(IsPendingAttack(view)){var a=view.Attack!;var s=Capture(units,a.AttackerSeat,a.TargetUnitId,a.SourceCardId);if(s!=null){s.PrepareStarted=now;waiting[a.AttackerSeat]=s;}}
        }
        private void Schedule(Shot shot,long id,float start,bool defeated)
        {
            shot.Id=id;
            // Current() presents one shot at a time. Queued preparation cannot elapse
            // invisibly behind the preceding shot; the first may retain its defense wait.
            float previousEnd=Shots.Select(s=>s.End).DefaultIfEmpty(shot.PrepareStarted).Max();
            shot.PrepareStarted=Mathf.Max(shot.PrepareStarted,previousEnd);
            float ready=shot.PrepareStarted+(shot.Support.Any(u=>u.Kind=="ranged")?1.8f:shot.Support.Any(u=>u.Kind=="heavy")?.6f:.15f);
            shot.Start=Mathf.Max(ready,start);shot.Defeated=defeated;Shots.Add(shot);
        }
        public float HeroImpact(int seat,float now)=>Shots.LastOrDefault(s=>s.Defeated && s.Target.Seat==seat && s.End>now)?.Impact ?? now;
        private static Shot? Capture(Dictionary<string,UnitState> at,int attacker,string target,string card)
        {
            if(!at.TryGetValue("hero:"+attacker,out var source) || !at.TryGetValue(target,out var victim))return null;
            var shot=new Shot{Source=Copy(source),Target=Copy(victim),CardId=card};
            if(victim.Kind=="hero")
            {
                var basis=MinionCombatBaseline.Sources(at.Values,victim);
                shot.Support=basis.EnemySupportSources.Distinct().Where(at.ContainsKey).Select(id=>Copy(at[id])).ToList();shot.Guard=basis.FriendlyGuardSources.Distinct().Where(at.ContainsKey).Select(id=>Copy(at[id])).ToList();
            }
            return shot;
        }
        private static UnitState Copy(UnitState u)=>new UnitState{Id=u.Id,Kind=u.Kind,Seat=u.Seat,Team=u.Team,Position=u.Position};
    }
}

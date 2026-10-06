using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Ai
{
    public sealed class RandomPolicy : IPolicy
    {
        private readonly StableRandom random;
        public RandomPolicy(int seed) { random=new StableRandom(seed); }
        public string Choose(Observation observation,IReadOnlyList<Candidate> actions) => actions[random.Next(actions.Count)].Id;
    }
    public sealed class SimplePolicy : IPolicy
    {
        private readonly StableRandom random;
        private readonly Dictionary<string,(string family,int value,int move)> cards;
        private readonly Dictionary<Hex,string> regions;
        public SimplePolicy(ContentCatalog catalog,int seed)
        {
            random=new StableRandom(seed); var ids=new StableIds(catalog);
            cards=catalog.Cards.ToDictionary(c=>ids.Card(c.Id),c=>(c.PrimaryFamily,c.PrimaryValue,c.SecondaryMovement??0));
            regions=catalog.Cells.ToDictionary(c=>c.Position,c=>c.Region);
        }
        public string Choose(Observation o,IReadOnlyList<Candidate> actions)
        {
            string team=o.Players.Single(p=>p.Seat==o.Seat).Team;
            var enemies=o.Units.Where(u=>u.Team!=team).ToList();
            double Position(Hex at)
            {
                var targets=enemies.Where(u=>u.Kind!="hero").ToList(); if(targets.Count==0) targets=enemies;
                return (regions.TryGetValue(at,out var region) && region==o.CombatRegion?8:0) - (targets.Count==0?0:targets.Min(u=>u.Position.Distance(at)));
            }
            double Score(Candidate a)
            {
                if(a.Kind=="Defend") return a.SuccessfulDefense?100:0;
                if(a.Kind=="DeclineDefense" || a.Kind=="DeclineRetaliationDiscard") return -100;
                if(a.Kind=="Pass") return -80;
                if(a.Value=="skip") return -20;
                if(a.Kind=="BeginPrimary") return a.ImmediateSkip?-60:20;
                if(a.Kind=="ChooseAttackTarget")
                {
                    var target=o.Units.SingleOrDefault(u=>u.Id==a.Value);
                    return target==null?0:target.Kind=="heavy"?50:target.Kind=="hero"?15:40;
                }
                if(a.Kind=="SelectCard" && cards.TryGetValue(a.Value,out var card))
                {
                    var self=o.Units.SingleOrDefault(u=>u.Seat==o.Seat);
                    int distance=self==null || enemies.Count==0?99:enemies.Min(u=>u.Position.Distance(self.Position));
                    return card.family=="attack" && distance<=2?30+card.value:card.family=="movement"?15+card.value:card.move>0?10+card.move:1;
                }
                if(a.HasDestination) return Position(a.Destination);
                return 0;
            }
            double best=actions.Max(Score);
            var tied=actions.Where(a=>Math.Abs(Score(a)-best)<0.001).ToList();
            return tied[random.Next(tied.Count)].Id;
        }
    }
}

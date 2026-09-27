#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;

namespace Goa2.Rules
{
    public static class PushRules
    {
        public static PushResult? AwayFromAdjacent(ContentCatalog catalog,GameState state,UnitState source,UnitState target,int distance)
        {
            if(distance<1 || source.Position.Distance(target.Position)!=1)return null;
            var result=new PushResult();result.Path.Add(target.Position);
            var direction=new Hex(target.Position.X-source.Position.X,target.Position.Y-source.Position.Y);
            var occupied=new HashSet<Hex>(state.Units.Select(u=>u.Position));
            bool terrain=state.EngineVersion>=74 && UltimateRules.CanTraverseObstacles(catalog,state,target);
            bool units=state.EngineVersion>=74 && (terrain || EffectRules.CanTraverseUnits(state,target));
            if(terrain || units)return ThroughObstacles(catalog,target.Position,direction,distance,occupied,terrain,units);
            var current=target.Position;
            for(int i=0;i<distance;i++)
            {
                var next=new Hex(current.X+direction.X,current.Y+direction.Y);var cell=catalog.Cell(next);
                if(cell==null){result.StopReason="map_edge";break;}
                if(cell.Obstacle){result.StopReason="obstacle";break;}
                if(occupied.Contains(next)){result.StopReason="occupied";break;}
                result.Path.Add(next);current=next;
            }
            return result;
        }
        private static PushResult ThroughObstacles(ContentCatalog catalog,Hex origin,Hex direction,int distance,HashSet<Hex> occupied,bool terrain,bool units)
        {
            var result=new PushResult();result.Path.Add(origin);
            var segment=new List<Hex>();var current=origin;string blocked="";
            for(int i=0;i<distance;i++)
            {
                var next=new Hex(current.X+direction.X,current.Y+direction.Y);var cell=catalog.Cell(next);
                if(cell==null){result.StopReason=blocked!=""?blocked:"map_edge";return result;}
                string obstacle=cell.Obstacle?"obstacle":occupied.Contains(next)?"occupied":"";
                if(cell.Obstacle && !terrain || occupied.Contains(next) && !units)
                {result.StopReason=blocked!=""?blocked:obstacle;return result;}
                segment.Add(next);current=next;
                if(obstacle!=""){if(blocked=="")blocked=obstacle;continue;}
                // Commit traversed obstacles only once an empty endpoint has been reached.
                result.Path.AddRange(segment);segment.Clear();blocked="";
            }
            result.StopReason=blocked;return result;
        }
    }
}

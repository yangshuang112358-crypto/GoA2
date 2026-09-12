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
    }
}

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
namespace Goa2.Presentation.UI3D
{
    public sealed partial class Board3DScene
    {
        private readonly Dictionary<string,UnitState> visualGhosts=new Dictionary<string,UnitState>();
        private readonly List<(Transform transform,Vector3 scale,string id)> ghostParts=new List<(Transform,Vector3,string)>();
        private readonly List<(LineRenderer line,CombatPresentationTimeline.Shot shot,Vector3 from)> projectiles=new List<(LineRenderer,CombatPresentationTimeline.Shot,Vector3)>();
        private void RememberGhost(UnitState unit,int previousChildren)
        {
            for(int i=previousChildren;i<host.transform.childCount;i++){var part=host.transform.GetChild(i);ghostParts.Add((part,part.localScale,unit.Id.Substring("ghost:".Length)));part.gameObject.name="defeated visual ghost "+unit.Id;}
        }
        private void BuildCombatProjectiles()
        {
            foreach(var shot in state.Presentation.Combat.Shots.Where(s=>s.End>Time.realtimeSinceStartup))
                foreach(var bow in shot.Support.Where(u=>u.Kind=="ranged"))
                    projectiles.Add((MinionLine("released arrow "+shot.Id+" "+bow.Id,.035f,new Color(.78f,.58f,.23f)),shot,Board3DGeometry.World(bow.Position,MinionHeight("ranged")*.66f)));
        }
        private void AnimateCombat()
        {
            float now=Time.realtimeSinceStartup;
            foreach(var part in ghostParts)
            {
                var shot=state.Presentation.Combat.Shots.LastOrDefault(s=>s.Target.Id==part.id);
                float age=shot==null?10:now-shot.Impact;part.transform.gameObject.SetActive(age<.58f);
                if(age>=0)part.transform.localScale=part.scale*Mathf.Clamp01(1-age/.58f);
            }
            foreach(var item in projectiles)
            {
                float age=now-item.shot.Start,t=(age-.12f)/.50f;item.line.enabled=t>=0 && t<=1;
                if(!item.line.enabled)continue;
                var target=Board3DGeometry.World(item.shot.Target.Position,HeroHeight*.58f);var at=Vector3.Lerp(item.from,target,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*.26f;
                var direction=(target-item.from).normalized;item.line.positionCount=2;item.line.SetPositions(new[]{at-direction*.46f,at});
            }
        }
    }
}

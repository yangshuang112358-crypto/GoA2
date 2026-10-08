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
        private readonly List<(GameObject arrow,CombatPresentationTimeline.Shot shot,string unit)> projectiles=new List<(GameObject,CombatPresentationTimeline.Shot,string)>();
        private GameObject MinionArrow(string name,Team team)
        {
            var model=Resources.Load<GameObject>("UI3D/Minions/Arrow");
            var wrapper=new GameObject(name){layer=Layer,hideFlags=HideFlags.HideAndDontSave};wrapper.transform.SetParent(host.transform,false);
            if(model==null)throw new System.InvalidOperationException("Missing independent arrow model");
            var mesh=Object.Instantiate(model,wrapper.transform,false);
            foreach(var t in mesh.GetComponentsInChildren<Transform>(true)){t.gameObject.layer=Layer;t.gameObject.hideFlags=HideFlags.HideAndDontSave;}
            foreach(var renderer in mesh.GetComponentsInChildren<Renderer>())renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>MinionMaterial(m.name,team)).ToArray();
            wrapper.SetActive(false);return wrapper;
        }
        private static void PlaceArrow(GameObject arrow,Vector3 tail,Vector3 direction,float length)
        {
            arrow.transform.position=tail;
            arrow.transform.rotation=Quaternion.LookRotation(direction,Mathf.Abs(Vector3.Dot(direction,Vector3.up))>.98f?Vector3.forward:Vector3.up);
            arrow.transform.localScale=Vector3.one*length;
        }
        private void RememberGhost(UnitState unit,int previousChildren)
        {
            for(int i=previousChildren;i<host.transform.childCount;i++){var part=host.transform.GetChild(i);ghostParts.Add((part,part.localScale,unit.Id.Substring("ghost:".Length)));part.gameObject.name="defeated visual ghost "+unit.Id;}
        }
        private void BuildCombatProjectiles()
        {
            foreach(var shot in state.Presentation.Combat.Shots.Where(s=>s.End>Time.realtimeSinceStartup))
                foreach(var bow in shot.Support.Where(u=>u.Kind=="ranged"))
                    projectiles.Add((MinionArrow("released arrow "+shot.Id+" "+bow.Id,bow.Team),shot,bow.Id));
        }
        private void AnimateCombat(float now)
        {
            foreach(var part in ghostParts)
            {
                var shot=state.Presentation.Combat.Shots.LastOrDefault(s=>s.Target.Id==part.id);
                float age=shot==null?10:now-shot.Impact;part.transform.gameObject.SetActive(age<.58f);
                if(age>=0)part.transform.localScale=part.scale*Mathf.Clamp01(1-age/.58f);
            }
            foreach(var item in projectiles)
            {
                float t=(now-item.shot.Release)/(item.shot.Impact-item.shot.Release);
                bool visible=t>=0 && t<1 && item.shot.Arrows.ContainsKey(item.unit);item.arrow.SetActive(visible);
                if(!visible)continue;
                var launch=item.shot.Arrows[item.unit];
                var target=Board3DGeometry.World(item.shot.Target.Position,HeroHeight*.58f);
                var tip=launch.Tail+launch.Direction*launch.Length;
                // A cubic begins with the held arrow's exact direction and ends at the
                // victim. Its tail at t=0 is precisely the former nock, never the unit center.
                float distance=Vector3.Distance(tip,target);
                var b=tip+launch.Direction*distance*.32f;
                var c=Vector3.Lerp(tip,target,.70f)+Vector3.up*.12f;
                float u=1-t;
                var at=u*u*u*tip+3*u*u*t*b+3*u*t*t*c+t*t*t*target;
                var direction=(3*u*u*(b-tip)+6*u*t*(c-b)+3*t*t*(target-c)).normalized;
                PlaceArrow(item.arrow,at-direction*launch.Length,direction,launch.Length);
            }
        }
    }
}

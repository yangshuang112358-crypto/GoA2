#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
namespace Goa2.Presentation.UI3D
{
    public sealed class MinionPoseMotion
    {
        public float Support,Guard,Raised;
        public float LastTime=-1,Changed;
        public string Pose="idle";
        public bool Rise;
        public void Set(string pose,bool rise,float now){if(Pose!=pose || Rise!=rise){Changed=now;Pose=pose;Rise=rise;}}
        public void Advance(float now){float dt=LastTime<0?0:Mathf.Clamp(now-LastTime,0,.05f);LastTime=now;float t=1-Mathf.Exp(-dt*7);Raised=Mathf.Lerp(Raised,Rise?1:0,t);Support=Mathf.Lerp(Support,Pose=="support"?1:0,t);Guard=Mathf.Lerp(Guard,Pose=="guard"?1:0,t);}
    }
    public sealed partial class Board3DScene
    {
        private sealed class MinionRig
        {
            public Transform Root=null!;public string Id="",Kind="";
            public readonly Dictionary<string,(Transform bone,Quaternion rotation,Vector3 position,Vector3 scale)> Bones=new Dictionary<string,(Transform,Quaternion,Vector3,Vector3)>();
            public MinionPoseMotion Motion=null!;
        }
        private readonly List<MinionRig> minionRigs=new List<MinionRig>();
        private void RegisterMinionRig(GameObject instance,string id,string kind)
        {
            if(!state.Presentation.MinionMotions.TryGetValue(id,out var motion))state.Presentation.MinionMotions[id]=motion=new MinionPoseMotion();
            var rig=new MinionRig{Id=id,Kind=kind,Root=instance.transform,Motion=motion};
            foreach(var bone in instance.GetComponentsInChildren<Transform>())if(bone.name=="Hips" || bone.name=="Spine" || bone.name.StartsWith("Upper") || bone.name.StartsWith("Lower") || bone.name.StartsWith("SpiderLeg."))rig.Bones[bone.name]=(bone,bone.localRotation,bone.localPosition,bone.localScale);
            minionRigs.Add(rig);
        }
        private void ConfigureMinionPoses(GameView view,Hex? selected)
        {
            var target=view.Attack==null?null:view.Units.FirstOrDefault(u=>u.Id==view.Attack.TargetUnitId);
            if(target==null && selected.HasValue){var unit=view.Units.FirstOrDefault(u=>u.Position==selected.Value && u.Kind=="hero");if(unit!=null && (view.AttackTargets.Contains(unit.Id) || view.PrimaryPreview?.Kind=="attack_target" && view.PrimaryPreview.Targets.Contains(unit.Id)))target=unit;}
            var influence=target==null?new AttackBreakdown():MinionCombatBaseline.Sources(view.Units,target);
            foreach(var rig in minionRigs){
                var unit=view.Units.First(u=>u.Id==rig.Id);bool support=influence.EnemySupportSources.Contains(rig.Id),guard=influence.FriendlyGuardSources.Contains(rig.Id);
                string pose=guard?"guard":support?"support":"idle";
                if(view.Sandbox && state.MinionPreviewPose!="")pose=state.MinionPreviewPose;
                bool rise=rig.Kind=="heavy" && (view.RemovableMinions.Contains(rig.Id) || pose!="idle");
                rig.Motion.Set(pose,rise,Time.realtimeSinceStartup);
                if(support || guard){
                    var hue=ColorOf(unit.Team==Team.Blue?"#48B7FF":"#FF4E61");
                    Add(Own(Board3DGeometry.Ring(48,.78f)),Board3DGeometry.World(unit.Position,.11f),Vector3.one*.92f,hue,"base combat minion highlight "+unit.Id);
                    foreach(var renderer in rig.Root.GetComponentsInChildren<Renderer>())renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>{var clone=Own(new Material(m));clone.color=Color.Lerp(m.color,hue,.30f);clone.SetFloat("_Emission",.3f);return clone;}).ToArray();
                }
            }
        }
        private void AnimateMinionPoses()
        {
            float now=Time.realtimeSinceStartup;
            foreach(var rig in minionRigs){
                var m=rig.Motion;m.Advance(now);
                foreach(var pair in rig.Bones.Values){pair.bone.localRotation=pair.rotation;pair.bone.localPosition=pair.position;pair.bone.localScale=pair.scale;}
                void Turn(string name,float degrees,Vector3 axis){if(rig.Bones.TryGetValue(name,out var b))b.bone.rotation=Quaternion.AngleAxis(degrees,axis)*b.bone.rotation;}
                void Shift(string name,Vector3 delta){if(rig.Bones.TryGetValue(name,out var b))b.bone.position+=delta;}
                var right=rig.Root.right;var forward=rig.Root.forward;
                float ready=rig.Kind=="heavy"?Mathf.InverseLerp(.35f,.8f,m.Raised):1;
                float guard=m.Guard*ready,support=m.Support*ready;
                if(rig.Kind=="heavy"){
                    Shift("Hips",Vector3.up*(-.15f+.40f*m.Raised));
                    foreach(var pair in rig.Bones.Where(p=>p.Key.StartsWith("SpiderLeg.")))pair.Value.bone.localScale=pair.Value.scale*Mathf.Lerp(.06f,1,m.Raised);
                }else if(rig.Kind=="melee"){
                    Shift("Hips",Vector3.down*.24f*guard+forward*.15f*support);
                    Turn("UpperLeg.L",32*guard,right);Turn("LowerLeg.L",-68*guard,right);Turn("UpperLeg.R",-28*guard,right);Turn("LowerLeg.R",45*guard,right);
                }
                if(rig.Kind=="ranged"){
                    float draw=Mathf.Clamp01((now-m.Changed)/.75f);float reach=Mathf.Sin(draw*Mathf.PI)*support;
                    Turn("UpperArm.L",-75*support,right);Turn("LowerArm.L",-15*support,right);
                    Turn("UpperArm.R",(-60*support-75*reach),right);Turn("LowerArm.R",-50*support,rig.Root.up);
                }else{
                    Turn("UpperArm.L",-62*guard,right);Turn("LowerArm.L",-36*guard,right);
                    Turn("UpperArm.R",-40*support,right);Turn("LowerArm.R",-32*support,right);Turn("Spine",-8*guard,right);
                }
            }
        }
    }
}

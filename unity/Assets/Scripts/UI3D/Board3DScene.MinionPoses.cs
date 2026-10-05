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
        public Quaternion Facing=Quaternion.identity,TargetFacing=Quaternion.identity;
        public bool FacingInitialized;
        public void Set(string pose,bool rise,float now){if(Pose!=pose || Rise!=rise){Changed=now;Pose=pose;Rise=rise;}}
        public void Advance(float now){float dt=LastTime<0?0:Mathf.Clamp(now-LastTime,0,.05f);LastTime=now;float t=1-Mathf.Exp(-dt*7);Raised=Mathf.Lerp(Raised,Rise?1:0,t);Support=Mathf.Lerp(Support,Pose=="support"?1:0,t);Guard=Mathf.Lerp(Guard,Pose=="guard"?1:0,t);Facing=Quaternion.Slerp(Facing,TargetFacing,t);}
    }
    public sealed partial class Board3DScene
    {
        private sealed class MinionRig
        {
            public Transform Root=null!;public string Id="",Kind="";
            public readonly Dictionary<string,(Transform bone,Quaternion rotation,Vector3 position,Vector3 scale)> Bones=new Dictionary<string,(Transform,Quaternion,Vector3,Vector3)>();
            public MinionPoseMotion Motion=null!;
            public Vector3 Center;
            public float Handedness=1;
            public LineRenderer? String,Arrow;
            public string IdlePose="idle";public bool IdleRise;
            public Quaternion IdleFacing;
        }
        private readonly List<MinionRig> minionRigs=new List<MinionRig>();
        private void RegisterMinionRig(GameObject instance,string id,string kind)
        {
            if(!state.Presentation.MinionMotions.TryGetValue(id,out var motion))state.Presentation.MinionMotions[id]=motion=new MinionPoseMotion();
            var rig=new MinionRig{Id=id,Kind=kind,Root=instance.transform,Motion=motion};
            foreach(var bone in instance.GetComponentsInChildren<Transform>())if(bone.name=="Hips" || bone.name=="Spine" || bone.name=="Head" || bone.name.StartsWith("Upper") || bone.name.StartsWith("Lower") || bone.name.StartsWith("Hand.") || bone.name.StartsWith("Weapon.") || bone.name.StartsWith("SpiderLeg.") || bone.name.StartsWith("Bow"))rig.Bones[bone.name]=(bone,bone.localRotation,bone.localPosition,bone.localScale);
            rig.Handedness=Mathf.Sign(Vector3.Dot(rig.Bones["Hand.R"].bone.position-rig.Bones["Hand.L"].bone.position,rig.Root.right));
            if(!motion.FacingInitialized){motion.FacingInitialized=true;motion.Facing=motion.TargetFacing=instance.transform.rotation;}
            if(kind=="ranged") {rig.String=MinionLine("bow string "+id,.012f,new Color(.84f,.78f,.59f));rig.Arrow=MinionLine("nocked arrow "+id,.022f,new Color(.67f,.42f,.18f));}
            minionRigs.Add(rig);
        }
        private LineRenderer MinionLine(string name,float width,Color color)
        {
            var go=new GameObject(name){layer=Layer,hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(host.transform,false);var line=go.AddComponent<LineRenderer>();line.useWorldSpace=true;line.startWidth=line.endWidth=width;line.numCapVertices=2;
            line.sharedMaterial=Own(new Material(Shader.Find("Sprites/Default")){color=color});line.startColor=line.endColor=Color.white;return line;
        }
        private void ConfigureMinionPoses(GameView view,Hex? selected)
        {
            var target=view.Attack==null?null:view.Units.FirstOrDefault(u=>u.Id==view.Attack.TargetUnitId);
            if(target==null && selected.HasValue){var unit=view.Units.FirstOrDefault(u=>u.Position==selected.Value && u.Kind=="hero");if(unit!=null && (view.AttackTargets.Contains(unit.Id) || view.PrimaryPreview?.Kind=="attack_target" && view.PrimaryPreview.Targets.Contains(unit.Id)))target=unit;}
            var influence=target==null?new AttackBreakdown():MinionCombatBaseline.Sources(view.Units,target);
            int? attackerSeat=view.Attack?.AttackerSeat ?? view.ActiveSeat;
            var attacker=attackerSeat.HasValue?view.Units.FirstOrDefault(u=>u.Seat==attackerSeat):null;
            foreach(var rig in minionRigs){
                var unit=view.Units.FirstOrDefault(u=>u.Id==rig.Id) ?? visualGhosts[rig.Id];bool support=influence.EnemySupportSources.Contains(rig.Id),guard=influence.FriendlyGuardSources.Contains(rig.Id);
                string pose=guard?"guard":support?"support":"idle";
                if(view.Sandbox && state.MinionPreviewPose!="")pose=state.MinionPreviewPose;
                rig.Center=Board3DGeometry.World(unit.Position,.04f);
                var facingTarget=guard?attacker:support?target:null;
                if(view.Sandbox && state.MinionPreviewPose!="")facingTarget=view.Units.Where(u=>u.Team!=unit.Team).OrderBy(u=>u.Position.Distance(unit.Position)).FirstOrDefault();
                Vector3 direction=facingTarget==null?Vector3.zero:Board3DGeometry.World(facingTarget.Position)-Board3DGeometry.World(unit.Position);
                rig.Motion.TargetFacing=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction,Vector3.up):Quaternion.Euler(0,unit.Team==Team.Blue?0:180,0);
                bool rise=rig.Kind=="heavy" && (view.RemovableMinions.Contains(rig.Id) || pose!="idle");
                rig.Motion.Set(pose,rise,Time.realtimeSinceStartup);
                rig.IdlePose=pose;rig.IdleRise=rise;rig.IdleFacing=rig.Motion.TargetFacing;
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
                var shot=state.Presentation.Combat.Current(now);
                bool attacking=shot!=null && shot.Support.Any(u=>u.Id==rig.Id),defending=shot!=null && shot.Guard.Any(u=>u.Id==rig.Id);
                var m=rig.Motion;
                if(attacking || defending)
                {
                    var target=defending?shot!.Source:shot!.Target;var direction=Board3DGeometry.World(target.Position)-rig.Center;direction.y=0;
                    m.TargetFacing=Quaternion.LookRotation(direction,Vector3.up);m.Set(defending?"guard":"support",rig.Kind=="heavy",now);
                }
                else {m.TargetFacing=rig.IdleFacing;m.Set(rig.IdlePose,rig.IdleRise,now);}
                m.Advance(now);
                rig.Root.rotation=m.Facing;
                foreach(var pair in rig.Bones.Values){pair.bone.localRotation=pair.rotation;pair.bone.localPosition=pair.position;pair.bone.localScale=pair.scale;}
                void Turn(string name,float degrees,Vector3 axis){if(rig.Bones.TryGetValue(name,out var b))b.bone.rotation=Quaternion.AngleAxis(degrees,axis)*b.bone.rotation;}
                void Shift(string name,Vector3 delta){if(rig.Bones.TryGetValue(name,out var b))b.bone.position+=delta;}
                // FBX changes handedness. Use the model's actual sword-hand side,
                // rather than moving that arm across the torso toward Unity +X.
                var right=rig.Root.right*rig.Handedness;var forward=rig.Root.forward;
                float ready=rig.Kind=="heavy"?Mathf.InverseLerp(.35f,.8f,m.Raised):1;
                float guard=m.Guard*ready,support=m.Support*ready;
                if(rig.Kind=="heavy"){
                    Shift("Hips",Vector3.up*(-.15f*(1-m.Raised)));
                    foreach(var pair in rig.Bones.Where(p=>p.Key.StartsWith("SpiderLeg.")))pair.Value.bone.localScale=pair.Value.scale*Mathf.Lerp(.06f,1,m.Raised);
                }else if(rig.Kind=="melee"){
                    Shift("Hips",Vector3.down*.22f*guard+forward*(.16f*guard+.10f*support));
                    Turn("UpperLeg.L",32*guard,rig.Root.right);Turn("LowerLeg.L",-68*guard,rig.Root.right);Turn("UpperLeg.R",-28*guard,rig.Root.right);Turn("LowerLeg.R",45*guard,rig.Root.right);
                }
                if(rig.Kind=="ranged"){
                    BowPose(rig,Mathf.Max(m.Support,m.Guard),attacking?now-shot!.PrepareStarted:now-m.Changed);
                    if(m.Pose=="idle")rig.Arrow!.enabled=false;
                    if(attacking && now>=shot!.Start+.12f){rig.Arrow!.enabled=false;var p=rig.String!.GetPosition(0);var q=rig.String.GetPosition(2);rig.String.SetPosition(1,(p+q)*.5f);}
                }else{
                    float h=MinionHeight(rig.Kind);Vector3 At(float x,float y,float z)=>rig.Center+right*x*h+Vector3.up*y*h+forward*z*h;
                    Turn("Spine",8*guard,rig.Root.right);
                    PoseHand(rig,"L",At(-.19f,rig.Kind=="heavy"?.53f:.49f,.28f),guard,Quaternion.identity,At(-.40f,.42f,.10f));
                    Vector3 sword=rig.Kind=="heavy"?(-right+forward*.42f+Vector3.up*.10f).normalized:(forward+Vector3.up).normalized;
                    if(attacking && now>=shot!.Start)
                    {
                        float swing=Mathf.SmoothStep(0,1,Mathf.Clamp01((now-shot.Start)/.55f));
                        sword=rig.Kind=="heavy"?Quaternion.AngleAxis(-140*swing,Vector3.up)*sword:Vector3.Slerp(sword,(forward-Vector3.up*.8f).normalized,swing);
                    }
                    PoseHand(rig,"R",At(rig.Kind=="heavy"?.37f:.29f,rig.Kind=="heavy"?.59f:.58f,rig.Kind=="heavy"?.30f:.16f),support,Quaternion.FromToRotation(Vector3.up,sword),At(.48f,.48f,.02f));
                }
            }
        }
        private static void PoseHand(MinionRig rig,string side,Vector3 target,float weight,Quaternion orientation,Vector3 elbowPole)
        {
            if(weight<.001f || !rig.Bones.TryGetValue("UpperArm."+side,out var a) || !rig.Bones.TryGetValue("LowerArm."+side,out var b) || !rig.Bones.TryGetValue("Hand."+side,out var c))return;
            var upper=a.bone;var lower=b.bone;var hand=c.bone;var restRotation=hand.rotation;target=Vector3.Lerp(hand.position,target,weight);
            float l1=Vector3.Distance(upper.position,lower.position),l2=Vector3.Distance(lower.position,hand.position);var delta=target-upper.position;
            float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(l1-l2)+.001f,l1+l2-.001f);var direction=delta.normalized;
            if(direction.sqrMagnitude<.01f)return;
            var bend=Vector3.ProjectOnPlane(elbowPole-upper.position,direction).normalized;if(bend.sqrMagnitude<.01f)bend=rig.Root.right;
            float along=(l1*l1-l2*l2+distance*distance)/(2*distance);float outwards=Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
            var elbow=upper.position+direction*along+bend*outwards;
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,upper.position+direction*distance-lower.position)*lower.rotation;
            hand.rotation=Quaternion.Slerp(restRotation,orientation*restRotation,weight);
        }
        private void BowPose(MinionRig rig,float weight,float elapsed)
        {
            float h=MinionHeight(rig.Kind);var f=rig.Root.forward;var r=rig.Root.right*rig.Handedness;
            Vector3 At(float x,float y,float z)=>rig.Center+r*x*h+Vector3.up*y*h+f*z*h;
            // Open the shoulders into an archery stance. Keep the large hood
            // looking at the target and clear of the drawn string/arrow.
            var head=rig.Bones["Head"].bone;var headRotation=head.rotation;
            var torso=rig.Bones["Spine"].bone;torso.rotation=Quaternion.AngleAxis(rig.Handedness*45*weight,Vector3.up)*torso.rotation;
            head.rotation=headRotation;head.position+=(-f*.09f+Vector3.up*.025f)*h*weight;
            // Reach over the head to the quiver, bring the arrow around the shoulder,
            // nock in front, then draw the string back. No teleport between key poses.
            Vector3[] keys={At(.22f,.39f,.08f),At(.18f,.80f,-.10f),At(.22f,.86f,.04f),At(-.02f,.64f,.30f),At(.02f,.64f,.15f)};
            float t=Mathf.Clamp(elapsed/.44f,0,3.999f);int index=Mathf.FloorToInt(t);float blend=Mathf.SmoothStep(0,1,t-index);
            var draw=Vector3.Lerp(keys[index],keys[index+1],blend);
            PoseHand(rig,"L",At(-.02f,.64f,.35f),weight,Quaternion.identity,At(-.27f,.60f,.25f));
            PoseHand(rig,"R",draw,weight,Quaternion.identity,At(.37f,.68f,-.04f));
            var hand=rig.Bones["Hand.R"].bone.position;
            var top=rig.Bones["BowTip.Top"].bone.position;var bottom=rig.Bones["BowTip.Bottom"].bone.position;
            float pulled=weight*Mathf.SmoothStep(0,1,(elapsed-1.30f)/.46f);var nock=Vector3.Lerp((top+bottom)*.5f,hand,pulled);
            rig.String!.positionCount=3;rig.String.SetPositions(new[]{bottom,nock,top});
            bool carrying=weight>.05f && elapsed>.40f;rig.Arrow!.enabled=carrying;
            if(carrying){var shaftStart=elapsed<1.3f?hand:nock;var aim=(rig.Bones["BowGrip"].bone.position-nock).normalized;var axis=elapsed<1.3f?Vector3.Slerp(Vector3.up,f,Mathf.Clamp01((elapsed-.55f)/.75f)):aim;rig.Arrow.positionCount=2;rig.Arrow.SetPositions(new[]{shaftStart,shaftStart+axis*h*.48f});}
        }
    }
}

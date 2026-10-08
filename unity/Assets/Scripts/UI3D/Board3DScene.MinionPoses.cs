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
        public readonly ArcherMotion Bow=new ArcherMotion();
        public CombatPresentationTimeline.ArrowLaunch? HeldArrow;
        public float Swing;
        public void Set(string pose,bool rise,float now){if(Pose!=pose || Rise!=rise){Changed=now;Pose=pose;Rise=rise;}}
        public void Advance(float now){float dt=LastTime<0?0:Mathf.Max(0,now-LastTime);LastTime=now;float t=1-Mathf.Exp(-dt*7);Raised=Mathf.Lerp(Raised,Rise?1:0,t);Support=Mathf.Lerp(Support,Pose=="support"?1:0,t);Guard=Mathf.Lerp(Guard,Pose=="guard"?1:0,t);Facing=Quaternion.Slerp(Facing,TargetFacing,t);}
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
            public LineRenderer? String;
            public GameObject? Arrow;
            public Quaternion BowFrameInHand;
            public Quaternion DrawWristInRoot;
            public Vector3 GripOffset,ArrowRestOffset;
            public string IdlePose="idle";public bool IdleRise;
            public Quaternion IdleFacing;
        }
        private readonly List<MinionRig> minionRigs=new List<MinionRig>();
        private void RegisterMinionRig(GameObject instance,string id,string kind,Team team)
        {
            if(!state.Presentation.MinionMotions.TryGetValue(id,out var motion))state.Presentation.MinionMotions[id]=motion=new MinionPoseMotion();
            var rig=new MinionRig{Id=id,Kind=kind,Root=instance.transform,Motion=motion};
            foreach(var bone in instance.GetComponentsInChildren<Transform>())if(bone.name=="Hips" || bone.name=="Spine" || bone.name=="Head" || bone.name.StartsWith("Upper") || bone.name.StartsWith("Lower") || bone.name.StartsWith("Hand.") || bone.name.StartsWith("Weapon.") || bone.name.StartsWith("SpiderLeg.") || bone.name.StartsWith("Bow") || bone.name.StartsWith("Quiver"))rig.Bones[bone.name]=(bone,bone.localRotation,bone.localPosition,bone.localScale);
            rig.Handedness=Mathf.Sign(Vector3.Dot(rig.Bones["Hand.R"].bone.position-rig.Bones["Hand.L"].bone.position,rig.Root.right));
            if(!motion.FacingInitialized){motion.FacingInitialized=true;motion.Facing=motion.TargetFacing=instance.transform.rotation;}
            if(kind=="ranged") {
                rig.String=MinionLine("bow string "+id,.010f,new Color(.84f,.78f,.59f));
                rig.Arrow=MinionArrow("nocked arrow "+id,team);
                var hand=rig.Bones["Hand.L"].bone;var grip=rig.Bones["BowGrip"].bone.position;
                var top=rig.Bones["BowTip.Top"].bone.position;var bottom=rig.Bones["BowTip.Bottom"].bone.position;
                var frame=Quaternion.LookRotation(grip-(top+bottom)*.5f,(top-bottom).normalized);
                rig.BowFrameInHand=Quaternion.Inverse(hand.rotation)*frame;
                rig.GripOffset=Quaternion.Inverse(hand.rotation)*(grip-hand.position);
                rig.ArrowRestOffset=Quaternion.Inverse(frame)*(rig.Bones["BowRest"].bone.position-grip);
                rig.DrawWristInRoot=Quaternion.Inverse(rig.Root.rotation)*rig.Bones["Hand.R"].bone.rotation;
            }
            minionRigs.Add(rig);
        }
        private LineRenderer MinionLine(string name,float width,Color color)
        {
            var go=new GameObject(name){layer=Layer,hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(host.transform,false);var line=go.AddComponent<LineRenderer>();line.useWorldSpace=true;line.startWidth=line.endWidth=width;line.numCapVertices=2;
            line.sharedMaterial=Own(new Material(Shader.Find("Sprites/Default")){color=color});line.startColor=line.endColor=Color.white;return line;
        }
        private void ConfigureMinionPoses(GameView view,Hex? selected)
        {
            var target=CombatPresentationTimeline.IsPendingAttack(view)?view.Units.FirstOrDefault(u=>u.Id==view.Attack!.TargetUnitId):null;
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
                var targetFacing=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction,Vector3.up):Quaternion.Euler(0,unit.Team==Team.Blue?0:180,0);
                bool rise=rig.Kind=="heavy" && (view.RemovableMinions.Contains(rig.Id) || pose!="idle");
                // Only Animate chooses the final pose. A rebuild must not insert a one-frame idle
                // between an accepted attack and its still-running visual shot.
                rig.IdlePose=pose;rig.IdleRise=rise;rig.IdleFacing=targetFacing;
                if(support || guard){
                    var hue=ColorOf(unit.Team==Team.Blue?"#48B7FF":"#FF4E61");
                    Add(Own(Board3DGeometry.Ring(48,.78f)),Board3DGeometry.World(unit.Position,.11f),Vector3.one*.92f,hue,"base combat minion highlight "+unit.Id);
                    foreach(var renderer in rig.Root.GetComponentsInChildren<Renderer>())renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>{var clone=Own(new Material(m));clone.color=Color.Lerp(m.color,hue,.30f);clone.SetFloat("_Emission",.3f);return clone;}).ToArray();
                }
            }
        }
        private void AnimateMinionPoses(float now)
        {
            foreach(var rig in minionRigs){
                var shot=state.Presentation.Combat.Current(now);
                bool attacking=shot!=null && shot.Support.Any(u=>u.Id==rig.Id),defending=shot!=null && shot.Guard.Any(u=>u.Id==rig.Id);
                var m=rig.Motion;
                bool recovering=attacking && now>shot!.Start+.56f;
                if(attacking || defending)
                {
                    var target=defending?shot!.Source:shot!.Target;var direction=Board3DGeometry.World(target.Position)-rig.Center;direction.y=0;
                    if(direction.sqrMagnitude>.0001f)m.TargetFacing=Quaternion.LookRotation(direction,Vector3.up);
                    m.Set(recovering?"idle":defending?"guard":"support",rig.Kind=="heavy",now);
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
                    if(attacking && m.Bow.ShotId>=0 && m.Bow.ShotId!=shot!.Id)m.HeldArrow=null;
                    m.Bow.Advance(now,attacking || defending || rig.IdlePose!="idle",attacking?shot!.Id:-1,attacking?shot!.Release:float.PositiveInfinity);
                    BowPose(rig,attacking?shot:null);
                }else{
                    float h=MinionHeight(rig.Kind);var chest=rig.Bones["Spine"].bone.position;
                    Vector3 At(float x,float y,float z)=>new Vector3(chest.x,rig.Center.y,chest.z)+right*x*h+Vector3.up*y*h+forward*z*h;
                    Turn("Spine",8*guard,rig.Root.right);
                    PoseHand(rig,"L",At(-.19f,rig.Kind=="heavy"?.53f:.49f,.28f),guard,Quaternion.identity,At(-.40f,.42f,.10f));
                    Vector3 sword=rig.Kind=="heavy"?(-right+forward*.42f+Vector3.up*.10f).normalized:(forward+Vector3.up).normalized;
                    if(attacking && !recovering && now>=shot!.Start)
                    {
                        m.Swing=ArcherMotion.Ease((now-shot.Start)/.5f);
                    }
                    else if(support<.03f)m.Swing=0;
                    sword=rig.Kind=="heavy"?Quaternion.AngleAxis(-140*m.Swing,Vector3.up)*sword:Vector3.Slerp(sword,(forward-Vector3.up*.8f).normalized,m.Swing);
                    // Keep the heavy's hand reachable; the broad body is not the bounds center.
                    var shoulder=rig.Bones["UpperArm.R"].bone.position;
                    var swordTarget=rig.Kind=="heavy"?shoulder+right*.10f+Vector3.up*.05f+forward*.30f:At(.29f,.58f,.16f);
                    PoseHand(rig,"R",swordTarget,support,Quaternion.FromToRotation(Vector3.up,sword),At(.48f,.48f,.02f));
                }
            }
        }
        private static void PoseHand(MinionRig rig,string side,Vector3 target,float weight,Quaternion orientation,Vector3 elbowPole,bool absolute=false)
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
            hand.rotation=Quaternion.Slerp(restRotation,absolute?orientation:orientation*restRotation,weight);
        }
        private void BowPose(MinionRig rig,CombatPresentationTimeline.Shot? shot,bool releaseSnapshot=false)
        {
            var motion=rig.Motion.Bow;
            if(!releaseSnapshot && shot!=null && motion.Released && !shot.Arrows.ContainsKey(rig.Id) && rig.Motion.HeldArrow==null)
            {
                // A long frame may cross the entire final draw. Evaluate this shot's
                // full-draw rig once, instead of borrowing another shot's launch point.
                BowPose(rig,null,true);
                foreach(var bone in rig.Bones.Values){bone.bone.localRotation=bone.rotation;bone.bone.localPosition=bone.position;bone.bone.localScale=bone.scale;}
            }
            float p=releaseSnapshot?1:motion.Progress,w=releaseSnapshot?1:motion.Weight,h=MinionHeight(rig.Kind);
            bool released=motion.Released && !releaseSnapshot;
            if(!released && p<.24f)rig.Motion.HeldArrow=null;
            var f=rig.Root.forward;var r=rig.Root.right*rig.Handedness;
            var head=rig.Bones["Head"].bone;var headRotation=head.rotation;
            float draw=ArcherMotion.Segment(p,.67f,1);
            var torso=rig.Bones["Spine"].bone;var center=torso.position;
            torso.rotation=Quaternion.AngleAxis(rig.Handedness*(18+78*draw)*w,Vector3.up)*torso.rotation;
            head.rotation=headRotation;
            Vector3 At(float x,float y,float z)=>center+r*x*h+Vector3.up*y*h+f*z*h;
            var lShoulder=rig.Bones["UpperArm.L"].bone;var rShoulder=rig.Bones["UpperArm.R"].bone;
            float shoulderY=(lShoulder.position.y+rShoulder.position.y)*.5f;
            // Nocking happens with a close, slightly canted bow. Then shoulder alignment,
            // bow extension and draw-hand anchoring develop together.
            var nockAim=f;
            var aim=Vector3.Slerp(nockAim,f,draw);
            var bowUp=Vector3.Slerp((Vector3.up*.906f-r*.423f).normalized,Vector3.up,draw);
            var bowFrame=Quaternion.LookRotation(aim,bowUp);
            var wristRotation=bowFrame*Quaternion.Inverse(rig.BowFrameInHand);
            var closeGrip=new Vector3(center.x,shoulderY-.08f*h,center.z)+r*.02f*h+f*.26f*h;
            var anchor=new Vector3(center.x,shoulderY+.04f*h,center.z)+r*.22f*h-f*.055f*h;
            var fullGrip=anchor+f*.33f*h-Quaternion.LookRotation(f,Vector3.up)*rig.ArrowRestOffset;
            var grip=Vector3.Lerp(closeGrip,fullGrip,draw);
            PoseHand(rig,"L",grip-wristRotation*rig.GripOffset,w,wristRotation,lShoulder.position-r*.2f*h+f*.10f*h,true);
            float bend=22.8f*draw*w;
            if(released)bend*=1-ArcherMotion.Segment(motion.ReleaseAge,0,.065f);
            var bendAxis=Vector3.Cross(bowUp,aim).normalized;
            foreach(string side in new[]{"Top","Bottom"})if(rig.Bones.TryGetValue("BowLimb."+side,out var limb))
                limb.bone.rotation=Quaternion.AngleAxis(side=="Top"?-bend:bend,bendAxis)*limb.bone.rotation;
            var rest=rig.Bones["BowRest"].bone.position;
            var top=rig.Bones["BowTip.Top"].bone.position;var bottom=rig.Bones["BowTip.Bottom"].bone.position;
            // Put the brace nock at arrow-rest height on the actual tip-to-tip string.
            var brace=Vector3.Lerp(bottom,top,Mathf.InverseLerp(bottom.y,top.y,rest.y));
            var nock=Vector3.Lerp(brace,anchor,draw);
            var grab=rig.Bones["QuiverGrab"].bone.position;
            // Exit is an arrow-path marker, not an anatomically reachable hand target.
            float reach=Vector3.Distance(rShoulder.position,rig.Bones["LowerArm.R"].bone.position)+Vector3.Distance(rig.Bones["LowerArm.R"].bone.position,rig.Bones["Hand.R"].bone.position);
            Vector3 Reach(Vector3 goal)=>rShoulder.position+Vector3.ClampMagnitude(goal-rShoulder.position,reach*.965f);
            var exit=Reach(rig.Bones["QuiverExit"].bone.position+r*.07f*h);
            var over=Reach(At(.29f,.30f,.065f));
            var handRest=rig.Bones["Hand.R"].bone.position;
            Vector3 handTarget;
            if(p<.24f)handTarget=Vector3.Lerp(handRest,grab,ArcherMotion.Segment(p,0,.24f));
            else if(p<.43f)handTarget=Vector3.Lerp(grab,exit,ArcherMotion.Segment(p,.24f,.43f));
            else if(p<.54f)handTarget=Vector3.Lerp(exit,over,ArcherMotion.Segment(p,.43f,.54f));
            else if(p<.67f)handTarget=Vector3.Lerp(over,nock,ArcherMotion.Segment(p,.54f,.67f));
            else handTarget=nock;
            var drawWrist=rig.Root.rotation*rig.DrawWristInRoot;
            if(released)handTarget=nock-f*(.065f*h*ArcherMotion.Segment(motion.ReleaseAge,0,.16f));
            PoseHand(rig,"R",handTarget,w,drawWrist,rShoulder.position+r*.22f*h-f*.13f*h,true);
            // Use actual reachable hand coordinates for contact. IK reach is tested separately.
            var hand=rig.Bones["Hand.R"].bone.position;
            float attached=ArcherMotion.Segment(p,.60f,.67f);
            var pulled=Vector3.Lerp(brace,hand,attached);
            if(released)pulled=Vector3.Lerp(hand,brace,ArcherMotion.Segment(motion.ReleaseAge,0,.055f));
            rig.String!.positionCount=3;rig.String.SetPositions(new[]{bottom,pulled,top});
            float arrowLength=h*.43f;
            Vector3 direction=(rest-hand).normalized;
            if(p<.60f)direction=Vector3.Slerp(-Vector3.up,(rest-hand).normalized,ArcherMotion.Segment(p,.43f,.60f));
            var tail=hand-direction*(h*.20f*(1-ArcherMotion.Segment(p,.43f,.67f)));
            if(!released && p>.67f)rig.Motion.HeldArrow=new CombatPresentationTimeline.ArrowLaunch{Tail=tail,Direction=direction,Length=arrowLength};
            if(shot!=null && motion.Released && !shot.Arrows.ContainsKey(rig.Id))
                shot.Arrows[rig.Id]=rig.Motion.HeldArrow ?? new CombatPresentationTimeline.ArrowLaunch{Tail=hand,Direction=(rest-hand).normalized,Length=arrowLength};
            bool carrying=p>=.24f && w>.001f && !released;
            rig.Arrow!.SetActive(carrying);
            if(carrying)PlaceArrow(rig.Arrow,tail,direction,arrowLength);
        }
    }
}

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Goa2.Presentation.UI3D
{
    public sealed partial class Board3DScene
    {
        private readonly List<(Transform transform,Vector3 position,string kind,int index)> heroAuras=new List<(Transform,Vector3,string,int)>();
        private readonly List<(LineRenderer line,Vector3 position,int index)> heroLightning=new List<(LineRenderer,Vector3,int)>();
        public int ModeledHeroCount{get;private set;}
        private bool BuildHero(string id,Team team,Hex cell)
        {
            var asset=Resources.Load<GameObject>("UI3D/Heroes/"+id);if(asset==null)return false;
            var instance=Object.Instantiate(asset,host.transform,false);instance.name="hero model "+id;
            instance.transform.localPosition=Vector3.zero;instance.transform.localRotation=Quaternion.identity;instance.transform.localScale=Vector3.one;
            foreach(var child in instance.GetComponentsInChildren<Transform>(true)){child.gameObject.layer=Layer;child.gameObject.hideFlags=HideFlags.HideAndDontSave;}
            var renderers=instance.GetComponentsInChildren<Renderer>();if(renderers.Length==0){Destroy(instance);return false;}
            var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            float scale=HeroHeight/Mathf.Max(.001f,bounds.size.y);instance.transform.localScale=Vector3.one*scale;
            instance.transform.localRotation=Quaternion.Euler(0,team==Team.Blue?0:180,0);
            instance.transform.localPosition=Board3DGeometry.World(cell,.04f)-instance.transform.localRotation*(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale);
            foreach(var renderer in renderers){
                renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>{
                    string slot=source==null?"Steel":source.name.Split(' ')[0];
                    string color=slot switch{"WhiteArmor"=>"#E6E5DE","SkinYellow"=>"#D4A860","SkinGreen"=>"#67965C","Skin"=>"#D8AF8A","BrownCloth"=>"#765035","Leather"=>"#49382E","TealArmor"=>"#397D89","ShadowCloth"=>"#252839","RedBeard"=>"#AA4E25","Bronze"=>"#B39250","Glow"=>"#8CE6ED","Shadow"=>"#141924","Linen"=>"#C6C4B0",_=>"#647F90"};
                    var material=Own(new Material(Resources.Load<Shader>("UI3D/Minion")){name="Hero "+slot,color=ColorOf(color)});
                    ActorSurface.Configure(material,slot);return material;
                }).ToArray();
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var position=Board3DGeometry.World(cell);
            ContactShadow(cell,.72f);
            if(id=="arien" || id=="tigerclaw"){
                for(int i=0;i<3;i++){
                    var auraAsset=Resources.Load<GameObject>("UI3D/HeroFX/"+(id=="arien"?"WaterRibbon":"ShadowWisp"));
                    if(auraAsset==null)continue;
                    // Keep the FBX root's unit/axis conversion. Animate a clean
                    // wrapper; replacing the imported root scale shrinks the FX.
                    var aura=new GameObject("hero aura "+id).transform;aura.SetParent(host.transform,false);
                    Object.Instantiate(auraAsset,aura,false);
                    foreach(var child in aura.GetComponentsInChildren<Transform>(true)){child.gameObject.layer=Layer;child.gameObject.hideFlags=HideFlags.HideAndDontSave;}
                    var material=Own(new Material(Resources.Load<Shader>("UI3D/HeroAura")){color=ColorOf(id=="arien"?"#41CBD2":"#443855")});
                    material.SetFloat("_Opacity",id=="arien"?.55f:.52f);material.SetFloat("_Mode",id=="arien"?0:1);material.SetFloat("_Phase",i*1.7f);
                    foreach(var renderer in aura.GetComponentsInChildren<Renderer>()){renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
                    heroAuras.Add((aura,position,id,i));
                }
            }
            if(id=="wasp")for(int i=0;i<7;i++){
                var go=new GameObject("wasp electric arc"){layer=Layer,hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(host.transform,false);
                var line=go.AddComponent<LineRenderer>();line.positionCount=13;line.widthMultiplier=i<4?.016f:.009f;line.useWorldSpace=false;line.numCapVertices=2;line.textureMode=LineTextureMode.Stretch;
                line.widthCurve=new AnimationCurve(new Keyframe(0,.12f),new Keyframe(.25f,1),new Keyframe(.72f,.7f),new Keyframe(1,0));
                line.sharedMaterial=Own(new Material(Resources.Load<Shader>("UI3D/HeroAura")){color=ColorOf("#B3F0FF")});line.sharedMaterial.SetFloat("_Opacity",.85f);
                line.sharedMaterial.SetFloat("_Mode",2);
                heroLightning.Add((line,position,i));
            }
            ModeledHeroCount++;return true;
        }
        private void AnimateHeroAuras()
        {
            float now=Time.realtimeSinceStartup;
            foreach(var aura in heroAuras){
                bool water=aura.kind=="arien";
                aura.transform.localPosition=aura.position+Vector3.up*(water?.04f+aura.index*.016f:.035f+Mathf.Sin(now*.65f+aura.index)*.035f);
                float scale=water?.85f+aura.index*.1f:.94f+aura.index*.045f;
                aura.transform.localScale=Vector3.one*scale;
                aura.transform.localRotation=Quaternion.Euler(0,now*(water?22:11)+aura.index*120,0);
            }
            foreach(var arc in heroLightning){
                float phase=Mathf.Floor(now*9+arc.index*2.3f);
                float pulse=Mathf.Sin(now*8.3f+arc.index*2.1f);
                arc.line.enabled=pulse>-.38f;float angle=now*.35f+arc.index*Mathf.PI*.62f;
                for(int j=0;j<13;j++){
                    float t=j/12f,jitter=Mathf.Sin(j*17.7f+phase*2.9f+arc.index)*.095f*Mathf.Sin(t*Mathf.PI);
                    bool branch=arc.index>=4;float radius=(branch?.34f:.37f)+jitter+(branch?t*.22f:0);
                    arc.line.SetPosition(j,arc.position+new Vector3(Mathf.Cos(angle+t*1.7f)*radius,.28f+t*(branch?.65f:1.65f)+(branch?.6f:0),Mathf.Sin(angle+t*1.7f)*radius));
                }
            }
        }
    }
}

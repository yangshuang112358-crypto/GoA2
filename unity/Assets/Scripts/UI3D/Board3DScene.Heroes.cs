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
                    material.SetFloat("_Metallic",slot.Contains("Armor") || slot=="Steel" || slot=="Bronze"?.55f:.02f);return material;
                }).ToArray();
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var position=Board3DGeometry.World(cell);
            if(id=="arien" || id=="tigerclaw"){
                for(int i=0;i<4;i++){
                    var aura=Add(Own(Board3DGeometry.Ring(64,.78f)),position+Vector3.up*.08f,Vector3.one,ColorOf(id=="arien"?"#41CBD2":"#574875"),"hero aura "+id).transform;
                    var material=Own(new Material(Resources.Load<Shader>("UI3D/HeroAura")){color=ColorOf(id=="arien"?"#41CBD2":"#443855")});
                    material.SetFloat("_Opacity",id=="arien"?.46f:.30f);aura.GetComponent<Renderer>().sharedMaterial=material;
                    heroAuras.Add((aura,position,id,i));
                }
            }
            if(id=="wasp")for(int i=0;i<4;i++){
                var go=new GameObject("wasp electric arc"){layer=Layer,hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(host.transform,false);
                var line=go.AddComponent<LineRenderer>();line.positionCount=7;line.widthMultiplier=.018f;line.useWorldSpace=false;
                line.sharedMaterial=Own(new Material(Resources.Load<Shader>("UI3D/HeroAura")){color=ColorOf("#B3F0FF")});line.sharedMaterial.SetFloat("_Opacity",.85f);
                heroLightning.Add((line,position,i));
            }
            ModeledHeroCount++;return true;
        }
        private void AnimateHeroAuras()
        {
            float now=Time.realtimeSinceStartup;
            foreach(var aura in heroAuras){
                float t=(now*.24f+aura.index*.25f)%1;
                bool water=aura.kind=="arien";float radius=water?.32f+t*.5f:.4f+Mathf.Sin(t*Mathf.PI)*.22f;
                aura.transform.localPosition=aura.position+Vector3.up*(water?.045f+aura.index*.022f:.12f+t*1.8f);
                aura.transform.localScale=new Vector3(radius,1,radius);aura.transform.localRotation=Quaternion.Euler(water?0:12,now*(water?45:20)+aura.index*90,water?0:15);
            }
            foreach(var arc in heroLightning){
                float angle=now*1.1f+arc.index*Mathf.PI*.5f;
                for(int j=0;j<7;j++){
                    float phase=Mathf.Floor(now*12);float jitter=Mathf.Sin(j*17.7f+phase*2.9f+arc.index)*.12f;
                    arc.line.SetPosition(j,arc.position+new Vector3(Mathf.Cos(angle+j*.3f)*(.32f+jitter),.25f+j*.25f,Mathf.Sin(angle+j*.3f)*(.32f+jitter)));
                }
            }
        }
    }
}

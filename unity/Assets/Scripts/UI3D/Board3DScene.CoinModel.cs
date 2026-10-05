#nullable enable
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Goa2.Presentation.UI3D
{
    public sealed partial class Board3DScene
    {
        private Transform? BuildDecisionCoinModel(Vector3 origin)
        {
            var asset=Resources.Load<GameObject>("UI3D/DecisionCoin");if(asset==null)return null;
            var pivot=new GameObject("decision coin"){layer=Layer,hideFlags=HideFlags.HideAndDontSave};pivot.transform.SetParent(host.transform,false);pivot.transform.localPosition=origin;
            var instance=Object.Instantiate(asset,pivot.transform,false);instance.transform.localPosition=Vector3.zero;instance.transform.localRotation=asset.transform.localRotation;instance.transform.localScale=Vector3.one;
            foreach(var child in instance.GetComponentsInChildren<Transform>()){child.gameObject.layer=Layer;child.gameObject.hideFlags=HideFlags.HideAndDontSave;}
            var renderers=instance.GetComponentsInChildren<Renderer>();if(renderers.Length==0){Destroy(pivot);return null;}
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            float scale=.965f/Mathf.Max(bounds.size.x,bounds.size.z);var localCenter=bounds.center-origin;
            instance.transform.localScale=Vector3.one*scale;instance.transform.localPosition=-localCenter*scale;
            foreach(var renderer in renderers){renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>{
                string name=source==null?"":source.name;bool red=name.StartsWith("RedGem"),blue=name.StartsWith("BlueGem");
                var material=Own(new Material(Resources.Load<Shader>("UI3D/Coin")){color=ColorOf(red?"#C82642":blue?"#236BD6":name.StartsWith("Oxidized")?"#69502D":name.StartsWith("Polished")?"#F0D089":"#C89A47")});material.SetFloat("_Metallic",red||blue?.2f:.86f);return material;
            }).ToArray();}
            return pivot.transform;
        }
    }
}

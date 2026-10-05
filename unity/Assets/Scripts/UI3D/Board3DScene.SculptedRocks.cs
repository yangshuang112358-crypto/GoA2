#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Goa2.Domain;
using Object=UnityEngine.Object;
namespace Goa2.Presentation.UI3D
{
    public sealed partial class Board3DScene
    {
        [Serializable]private sealed class RockLayout{public float width,height,centerX,centerZ;}
        private readonly List<(Transform pivot,int side)> spiralRocks=new List<(Transform,int)>();
        private Transform? platformGlyph;
        private bool BuildSculptedRocks()
        {
            var rock=Resources.Load<GameObject>("UI3D/Terrain/ConnectedRocks");var half=Resources.Load<GameObject>("UI3D/Terrain/CentralSpiralHalf");var layout=Resources.Load<TextAsset>("UI3D/Terrain/rock-layout");
            if(rock==null || half==null || layout==null)return false;
            var dimensions=JsonUtility.FromJson<RockLayout>(layout.text);var center=new Vector3(dimensions.centerX,0,dimensions.centerZ);
            var rocks=InstantiateStone(rock,"connected rocks",dimensions.width,center);
            foreach(var cell in cells.Values.Where(c=>c.Obstacle))
            {
                var tile=Add(Own(Board3DGeometry.Prism(6,-30)),Board3DGeometry.World(cell.Position),new Vector3(1,.07f,1),ColorOf("#676D6B"),"rock hex foundation");tile.GetComponent<MeshRenderer>().sharedMaterial=TerrainMaterial("rock",ColorOf("#676D6B"));
            }
            for(int side=0;side<2;side++)
            {
                var pivot=new GameObject("spiral platform "+side){layer=Layer,hideFlags=HideFlags.HideAndDontSave};pivot.transform.SetParent(host.transform,false);pivot.transform.localPosition=center;
                var stone=Object.Instantiate(half,pivot.transform,false);stone.transform.localScale=Vector3.one;stone.transform.localPosition=Vector3.zero;
                var bounds=StoneBounds(stone);float factor=2.24f/Mathf.Max(bounds.size.x,bounds.size.z);stone.transform.localScale=Vector3.one*factor;
                StyleStone(stone,"central spiral half "+side);spiralRocks.Add((pivot.transform,side));
            }
            platformGlyph=Add(Own(Board3DGeometry.Ring(96,.94f)),center+Vector3.up*1.145f,new Vector3(.8f,1,.39f),ColorOf("#3C3830"),"coin platform engraving").transform;
            platformGlyph.localRotation=Quaternion.Euler(0,60,0);
            foreach(var entry in spiralRocks)entry.pivot.localRotation=Quaternion.Euler(0,entry.side*180,0);
            return true;
        }
        private GameObject InstantiateStone(GameObject asset,string name,float width,Vector3 center)
        {
            var instance=Object.Instantiate(asset,host.transform,false);instance.transform.localScale=Vector3.one;instance.transform.localPosition=Vector3.zero;
            var bounds=StoneBounds(instance);float scale=width/bounds.size.x;instance.transform.localScale=Vector3.one*scale;
            instance.transform.localPosition=center-new Vector3(bounds.center.x,bounds.min.y-.07f/scale,bounds.center.z)*scale;
            StyleStone(instance,name);return instance;
        }
        private static Bounds StoneBounds(GameObject go){var renderers=go.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);return bounds;}
        private void StyleStone(GameObject go,string name)
        {
            foreach(var t in go.GetComponentsInChildren<Transform>()){t.gameObject.layer=Layer;t.gameObject.hideFlags=HideFlags.HideAndDontSave;}
            foreach(var renderer in go.GetComponentsInChildren<Renderer>()){renderer.gameObject.name=name;renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>TerrainMaterial("rock",ColorOf("#7A817D"))).ToArray();}
        }
    }
}

using Goa2.Domain;
using UnityEngine;
namespace Goa2.Presentation.UI3D
{
    public sealed partial class Board3DScene
    {
        private Mesh contactMesh;
        private Material contactMaterial;
        private void ContactShadow(Hex cell, float radius)
        {
            if(contactMesh==null){
                contactMesh=Own(new Mesh{name="soft actor contact quad"});
                contactMesh.vertices=new[]{new Vector3(-1,0,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)};
                contactMesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};contactMesh.triangles=new[]{0,2,1,0,3,2};
                contactMesh.RecalculateNormals();contactMesh.RecalculateBounds();
                contactMaterial=Own(new Material(Resources.Load<Shader>("UI3D/ContactShadow")));
            }
            var shadow=Add(contactMesh,Board3DGeometry.World(cell,.023f),new Vector3(radius,1,radius),Color.black,"actor contact shadow");
            shadow.GetComponent<Renderer>().sharedMaterial=contactMaterial;
        }
    }
}

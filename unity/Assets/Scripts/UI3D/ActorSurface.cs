using UnityEngine;

namespace Goa2.Presentation.UI3D
{
    // Semantic material names survive FBX re-export. Optional maps are a deliberate
    // contract: no attempt to translate arbitrary Blender shader graphs at runtime.
    internal static class ActorSurface
    {
        public static void Configure(Material material, string slot)
        {
            bool metal=slot.Contains("Armor") || slot.StartsWith("Steel") || slot=="Bronze";
            bool skin=slot.StartsWith("Skin");
            bool leather=slot=="Leather" || slot=="BrownCloth";
            bool glow=slot=="Glow";
            material.SetFloat("_Surface",metal?0:skin?3:glow?4:leather?2:1);
            material.SetFloat("_Roughness",metal?(slot=="Bronze"?.34f:.43f):skin?.72f:glow?.17f:leather?.64f:.88f);
            material.SetFloat("_Metallic",metal?(slot=="WhiteArmor"?.27f:.72f):glow?.25f:0);
            material.SetFloat("_Emission",glow?.32f:0);
            var albedo=Resources.Load<Texture2D>("UI3D/ActorSurfaces/"+slot+"-albedo");
            var normal=Resources.Load<Texture2D>("UI3D/ActorSurfaces/"+slot+"-normal");
            var mask=Resources.Load<Texture2D>("UI3D/ActorSurfaces/"+slot+"-mask");
            if(albedo!=null && normal!=null && mask!=null)
            {
                material.SetTexture("_MainTex",albedo);material.SetTexture("_NormalMap",normal);material.SetTexture("_MaskMap",mask);
                material.SetFloat("_UseAuthoredMaps",1);
            }
        }
    }
}

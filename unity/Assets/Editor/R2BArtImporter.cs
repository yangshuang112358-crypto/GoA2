using UnityEditor;
using UnityEngine;

namespace Goa2.Presentation.Editor
{
    public sealed class R2BArtImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture(){
            if(!assetPath.StartsWith("Assets/Resources/UI3D/R2B/") && !assetPath.StartsWith("Assets/Resources/UI3D/CardArt/"))return;
            var importer=(TextureImporter)assetImporter;importer.textureType=TextureImporterType.Default;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=1024;
            importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
        }
    }
}

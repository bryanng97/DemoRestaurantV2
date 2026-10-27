using UnityEditor;
using UnityEngine;

namespace DemoRestaurant.EditorTools
{
    public sealed class NativeAvatarImportRules : AssetPostprocessor
    {
        private const string Folder="Assets/_Game/Resources/Art/RefProto/AvatarRuntime/";
        public override int GetPostprocessOrder()=>100;
        private void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith(Folder))return;
            var texture=(TextureImporter)assetImporter;
            texture.textureType=TextureImporterType.Default;texture.isReadable=true;
            texture.mipmapEnabled=false;texture.alphaIsTransparency=true;
            texture.textureCompression=TextureImporterCompression.Uncompressed;
            texture.npotScale=TextureImporterNPOTScale.None;
        }
        private void OnPreprocessModel()
        {
            if(!assetPath.StartsWith(Folder))return;
            var model=(ModelImporter)assetImporter;
            model.animationType=ModelImporterAnimationType.Legacy;
            model.importAnimation=true;model.materialImportMode=ModelImporterMaterialImportMode.None;
            model.globalScale=1;model.optimizeGameObjects=false;
        }
    }
}

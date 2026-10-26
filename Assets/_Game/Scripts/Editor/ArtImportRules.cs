using UnityEditor;
using UnityEngine;

namespace DemoRestaurant.EditorTools
{
    /// <summary>
    /// Import settings for every texture under Assets/_Game/**/Art/: single sprite, bottom-centre pivot,
    /// no mipmaps, high-quality compression. Runtime code fits sprites to their footprint, so PPU does not matter.
    /// </summary>
    public sealed class ArtImportRules : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Game/") || !assetPath.Contains("/Art/"))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.spritePixelsPerUnit = 100;
            if (assetPath.Contains("_loop_sheet_")) importer.npotScale = TextureImporterNPOTScale.None;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            settings.spriteMeshType = SpriteMeshType.Tight;
            importer.SetTextureSettings(settings);
        }
    }
}

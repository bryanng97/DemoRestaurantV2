using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace DemoRestaurant.Editor
{
    /// <summary>
    /// App icon and startup splash from the images in Assets/_Game/Branding:
    /// - app_icon.png (square, 1024 px): the icon on every platform;
    /// - splash_background.png (optional, landscape, e.g. the world-map key art): fills the splash screen;
    /// - splash_logo.png (optional, transparent): shown centred on the splash.
    /// Unity 6 lets every plan turn off the "Made with Unity" logo, so the splash shows only NOMU WORLD art.
    /// Replace the images and run the menu again to update.
    /// </summary>
    public static class BrandingMenu
    {
        private const string Folder = "Assets/_Game/Branding";
        private const string IconPath = Folder + "/app_icon.png";
        private const string BackgroundPath = Folder + "/splash_background.png";
        private const string LogoPath = Folder + "/splash_logo.png";
        private static readonly Color Cream = new Color32(242, 233, 218, 255); // the logo artwork's own paper colour

        [MenuItem("DemoRestaurant/Apply App Icon and Splash")]
        public static void Apply()
        {
            var icon = ImportIcon(IconPath);
            if (icon == null)
            {
                Debug.LogError($"Put a square 1024 px PNG at {IconPath}, then run DemoRestaurant > Apply App Icon and Splash.");
                return;
            }
            // The default icon is used by Android and iOS unless a platform overrides it.
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            var background = ImportSprite(BackgroundPath);
            var logo = ImportSprite(LogoPath);

            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Dolly;
            PlayerSettings.SplashScreen.backgroundColor = Cream;
            PlayerSettings.SplashScreen.background = background;
            PlayerSettings.SplashScreen.backgroundPortrait = null; // landscape-only game
            PlayerSettings.SplashScreen.blurBackgroundImage = false;
            PlayerSettings.SplashScreen.overlayOpacity = 0f;
            // With key art behind it the logo is optional; without any art, the cat logo on cream is the splash.
            PlayerSettings.SplashScreen.logos = logo != null
                ? new[] { PlayerSettings.SplashScreenLogo.Create(2.5f, logo) }
                : new PlayerSettings.SplashScreenLogo[0];

            AssetDatabase.SaveAssets();
            Debug.Log("Applied the NOMU WORLD app icon and splash screen (no Unity logo). " +
                      (background == null ? $"Add {BackgroundPath} for a full-screen splash picture." : "Splash uses " + BackgroundPath + "."));
        }

        private static Texture2D ImportIcon(string path)
        {
            if (!File.Exists(path)) return null;
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Sprite ImportSprite(string path)
        {
            if (!File.Exists(path)) return null;
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}

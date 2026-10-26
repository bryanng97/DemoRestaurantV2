using System.Linq;
using DemoRestaurant.Game.StressTest;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace DemoRestaurant.EditorTools
{
    /// <summary>One-click project setup so scenes and settings are reproducible instead of hand-edited.</summary>
    public static class ProjectSetupMenu
    {
        private const string StressScenePath = "Assets/_Game/Scenes/StressTest.unity";
        private const string ConceptArtFolder = "Assets/_Game/Resources/Art/Concept";
        private const string ConceptAtlasPath = "Assets/_Game/Art/Atlases/Concept.spriteatlasv2";

        [MenuItem("DemoRestaurant/Build Concept Sprite Atlas")]
        public static void BuildConceptAtlas()
        {
            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ConceptArtFolder);
            if (folder == null)
            {
                Debug.LogError($"{ConceptArtFolder} not found. Run Tools/SpriteSlicer/slice.ps1 first.");
                return;
            }

            // Re-apply ArtImportRules in case sprites were imported before the rules compiled.
            AssetDatabase.ImportAsset(ConceptArtFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            if (EditorSettings.spritePackerMode != SpritePackerMode.SpriteAtlasV2)
                EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ConceptAtlasPath));
            var atlas = new SpriteAtlasAsset();
            atlas.Add(new Object[] { folder });
            SpriteAtlasAsset.Save(atlas, ConceptAtlasPath);
            AssetDatabase.ImportAsset(ConceptAtlasPath, ImportAssetOptions.ForceUpdate);

            var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(ConceptAtlasPath);
            importer.packingSettings = new SpriteAtlasPackingSettings
            {
                enableRotation = false,
                enableTightPacking = false,
                padding = 4,
                enableAlphaDilation = true,
            };
            importer.textureSettings = new SpriteAtlasTextureSettings
            {
                generateMipMaps = false,
                filterMode = FilterMode.Bilinear,
                sRGB = true,
            };
            importer.SaveAndReimport();
            Debug.Log($"Built {ConceptAtlasPath} from {ConceptArtFolder}.");
        }

        [MenuItem("DemoRestaurant/Create Stress Test Scene")]
        public static void CreateStressTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (AssetDatabase.IsValidFolder(ConceptArtFolder))
                BuildConceptAtlas();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.78f, 0.62f);

            new GameObject("StressTest").AddComponent<StressTestBootstrap>();

            System.IO.Directory.CreateDirectory("Assets/_Game/Scenes");
            EditorSceneManager.SaveScene(scene, StressScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != StressScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(StressScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"Created {StressScenePath} and made it the first scene in Build Profiles. Press Play.");
        }

        [MenuItem("DemoRestaurant/Apply Mobile Player Settings")]
        public static void ApplyMobilePlayerSettings()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Draw the restaurant edge to edge (under punch-hole cameras and notches); the UI keeps itself inside
            // Screen.safeArea via the SafeArea component.
            PlayerSettings.Android.renderOutsideSafeArea = true;

            AssetDatabase.SaveAssets();
            Debug.Log("Applied mobile player settings: IL2CPP + ARM64 on Android, landscape only. " +
                      "Set the company name and application identifier in Player Settings before the first store build.");
        }
    }
}

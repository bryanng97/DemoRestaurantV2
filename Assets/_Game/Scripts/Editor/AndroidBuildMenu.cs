using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DemoRestaurant.EditorTools
{
    /// <summary>
    /// One-click Android builds of the stress test. Output goes to Builds/Android/ (git-ignored).
    /// Requires the Android Build Support module (with OpenJDK and Android SDK &amp; NDK) in Unity Hub.
    /// </summary>
    public static class AndroidBuildMenu
    {
        private const string StressScenePath = "Assets/_Game/Scenes/StressTest.unity";
        private const string OutputDir = "Builds/Android";

        /// <summary>Release-style performance test with owned assets; a private development APK with reference assets.</summary>
        [MenuItem("DemoRestaurant/Android/Build Stress Test APK")]
        public static void BuildRelease() => Build(development: false, run: false);

        /// <summary>Builds, installs and launches on the USB-connected phone (USB debugging on).</summary>
        [MenuItem("DemoRestaurant/Android/Build And Run On Phone")]
        public static void BuildAndRun() => Build(development: false, run: true);

        /// <summary>Development build with the Profiler attached, for finding what costs the frame time.</summary>
        [MenuItem("DemoRestaurant/Android/Build And Run (Development + Profiler)")]
        public static void BuildAndRunDevelopment() => Build(development: true, run: true);

        /// <summary>Private phone test with temporary reference art; no Profiler connection required.</summary>
        [MenuItem("DemoRestaurant/Android/Build Private Prototype APK")]
        public static void BuildPrivatePrototype() => Build(development: true, run: false, privatePrototype: true);

        public static void BuildCatalogMigrationPrototype()
        {
            PlayerSettings.bundleVersion = "0.2.0";
            PlayerSettings.Android.bundleVersionCode = 2;
            AssetDatabase.SaveAssets();
            Build(true, false, true, "DemoRestaurant-0.2.0-b2-private-prototype.apk");
        }

        private static void Build(bool development, bool run, bool privatePrototype = false, string outputName = null)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                EditorUtility.DisplayDialog("Android Build Support missing",
                    "Install it in Unity Hub: Installs > 6000.6.4f1 > Add modules > Android Build Support " +
                    "(tick OpenJDK and Android SDK & NDK Tools), then restart the Editor.", "OK");
                return;
            }
            if (!File.Exists(StressScenePath))
            {
                EditorUtility.DisplayDialog("Stress test scene missing",
                    "Run DemoRestaurant > Create Stress Test Scene first.", "OK");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("Could not switch the active build target to Android.");
                return;
            }

            ProjectSetupMenu.ApplyMobilePlayerSettings();
            EditorUserBuildSettings.buildAppBundle = false;

            bool connectProfiler = development && !privatePrototype;
            privatePrototype |= RefProtoBuildGuard.FindPrototypeResources().Length > 0;
            if (privatePrototype)
            {
                development = true;
                Debug.Log("Building a PRIVATE PROTOTYPE development APK for local device testing. " +
                    "Temporary reference assets are included; release builds remain blocked until they are replaced.");
            }

            Directory.CreateDirectory(OutputDir);
            string apk = outputName != null ? $"{OutputDir}/{outputName}" : privatePrototype ? $"{OutputDir}/DemoRestaurant-private-prototype.apk" :
                $"{OutputDir}/DemoRestaurant-stress{(development ? "-dev" : "")}.apk";
            if (File.Exists(apk))
                File.Delete(apk); // so a stale APK can't pass for a fresh one
            var options = BuildOptions.None;
            if (development) options |= BuildOptions.Development;
            if (connectProfiler) options |= BuildOptions.ConnectWithProfiler;
            if (run) options |= BuildOptions.AutoRunPlayer;

            BuildReport report;
            using (privatePrototype ? RefProtoBuildGuard.BeginPrivateAndroidBuild() : null)
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { StressScenePath },
                    locationPathName = apk,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = options,
                });
            }

            var summary = report.summary;
            // Unity can report Succeeded even when the Android packaging step threw (e.g. the Android
            // module was installed but the Editor wasn't restarted), so trust the file, not the flag.
            if (summary.result == BuildResult.Succeeded && !File.Exists(apk))
            {
                EditorUtility.DisplayDialog("APK was not created",
                    "Unity compiled the game but did not produce the APK. If you just installed Android Build " +
                    "Support, close and reopen the Editor from Unity Hub. Otherwise check the Console for the first red error.",
                    "OK");
                return;
            }

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Built {apk} ({summary.totalSize / (1024f * 1024f):0.0} MB) in {summary.totalTime.TotalSeconds:0}s.");
                if (!run && !Application.isBatchMode)
                    EditorUtility.RevealInFinder(apk);
            }
            else
            {
                Debug.LogError($"Android build {summary.result}: {summary.totalErrors} error(s). See the Console for details.");
            }
        }
    }
}

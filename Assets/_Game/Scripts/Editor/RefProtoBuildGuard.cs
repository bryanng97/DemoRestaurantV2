using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace DemoRestaurant.EditorTools
{
    /// <summary>
    /// Reference-game assets in any Resources/**/RefProto folder are shared for prototype development only.
    /// Ordinary player builds fail while they are present. Only a scoped, clearly named Android development
    /// build is permitted for private device testing; permission is never stored in preferences or build defines.
    /// </summary>
    public sealed class RefProtoBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        private static int _privateBuildDepth;

        public static string[] FindPrototypeResources(string assetsRoot = "Assets")
        {
            if (!Directory.Exists(assetsRoot)) return Array.Empty<string>();
            var folders = Array.FindAll(Directory.GetDirectories(assetsRoot, "RefProto", SearchOption.AllDirectories),
                path => path.Replace('\\', '/').IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0);
            Array.Sort(folders, StringComparer.Ordinal);
            return folders;
        }

        public static IDisposable BeginPrivateAndroidBuild() => new PrivateBuildScope();

        public static bool AllowsPrivateBuild(BuildTarget target, BuildOptions options, string outputPath) =>
            _privateBuildDepth > 0 && target == BuildTarget.Android &&
            (options & BuildOptions.Development) != 0 &&
            !string.IsNullOrEmpty(outputPath) &&
            Path.GetFileName(outputPath).EndsWith("-private-prototype.apk", StringComparison.OrdinalIgnoreCase);

        public static void ValidateBuild(string assetsRoot, BuildTarget target, BuildOptions options, string outputPath)
        {
            var folders = FindPrototypeResources(assetsRoot);
            if (folders.Length == 0 || AllowsPrivateBuild(target, options, outputPath)) return;
            throw new BuildFailedException($"Prototype-only reference assets found in {folders[0].Replace('\\', '/')}. " +
                "For private device testing use DemoRestaurant > Android > Build Private Prototype APK. " +
                "Replace the RefProto resources with your own assets before a release build.");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var summary = report.summary;
            ValidateBuild("Assets", summary.platform, summary.options, summary.outputPath);
        }

        private sealed class PrivateBuildScope : IDisposable
        {
            private bool _disposed;
            public PrivateBuildScope() => _privateBuildDepth++;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _privateBuildDepth--;
            }
        }
    }
}

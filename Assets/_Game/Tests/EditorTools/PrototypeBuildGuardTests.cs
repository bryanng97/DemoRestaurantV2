using System;
using System.IO;
using DemoRestaurant.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

namespace DemoRestaurant.Editor.Tests
{
    public sealed class PrototypeBuildGuardTests
    {
        private string _root;
        private const string PrivateApk = "Builds/Android/DemoRestaurant-private-prototype.apk";

        [SetUp] public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "DemoRestaurantGuardTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }
        [TearDown] public void TearDown()
        {
            string full = Path.GetFullPath(_root);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (!full.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("DemoRestaurantGuardTests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(full, true);
        }
        private void AddPrototype(string path = "_Game/Resources/Art/RefProto") =>
            Directory.CreateDirectory(Path.Combine(_root, path));

        [Test] public void OwnedAssetsDoNotBlockReleaseBuilds()
        {
            Assert.DoesNotThrow(() => RefProtoBuildGuard.ValidateBuild(_root, BuildTarget.Android, BuildOptions.None, "game.apk"));
        }
        [TestCase("_Game/Resources/Art/RefProto")]
        [TestCase("_Game/Resources/Audio/RefProto")]
        [TestCase("Resources/RefProto")]
        public void AnyResourceLocationBlocksAnOrdinaryBuild(string path)
        {
            AddPrototype(path);
            Assert.Throws<BuildFailedException>(() => RefProtoBuildGuard.ValidateBuild(_root, BuildTarget.Android, BuildOptions.None, "game.apk"));
            Assert.Throws<BuildFailedException>(() => RefProtoBuildGuard.ValidateBuild(_root, BuildTarget.Android, BuildOptions.Development, PrivateApk));
        }
        [Test] public void PrivateScopeOnlyAllowsNamedAndroidDevelopmentApks()
        {
            AddPrototype();
            using (RefProtoBuildGuard.BeginPrivateAndroidBuild())
            {
                Assert.DoesNotThrow(() => RefProtoBuildGuard.ValidateBuild(_root, BuildTarget.Android, BuildOptions.Development, PrivateApk));
                Assert.Throws<BuildFailedException>(() => RefProtoBuildGuard.ValidateBuild(_root, BuildTarget.Android, BuildOptions.None, PrivateApk));
                Assert.Throws<BuildFailedException>(() => RefProtoBuildGuard.ValidateBuild(_root, BuildTarget.Android, BuildOptions.Development, "game.apk"));
                Assert.Throws<BuildFailedException>(() => RefProtoBuildGuard.ValidateBuild(_root, BuildTarget.Android, BuildOptions.Development, "game.aab"));
                Assert.Throws<BuildFailedException>(() => RefProtoBuildGuard.ValidateBuild(_root, BuildTarget.StandaloneWindows64, BuildOptions.Development, PrivateApk));
            }
            Assert.Throws<BuildFailedException>(() => RefProtoBuildGuard.ValidateBuild(_root, BuildTarget.Android, BuildOptions.Development, PrivateApk));
        }
        [Test] public void ScopeCannotLeakAfterExceptionsOrDoubleDispose()
        {
            AddPrototype();
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (RefProtoBuildGuard.BeginPrivateAndroidBuild()) throw new InvalidOperationException("Test failure");
            });
            Assert.IsFalse(RefProtoBuildGuard.AllowsPrivateBuild(BuildTarget.Android, BuildOptions.Development, PrivateApk));
            using (var outer = RefProtoBuildGuard.BeginPrivateAndroidBuild())
            {
                using (var inner = RefProtoBuildGuard.BeginPrivateAndroidBuild()) { inner.Dispose(); }
                Assert.IsTrue(RefProtoBuildGuard.AllowsPrivateBuild(BuildTarget.Android, BuildOptions.Development, PrivateApk));
                outer.Dispose();
            }
            Assert.IsFalse(RefProtoBuildGuard.AllowsPrivateBuild(BuildTarget.Android, BuildOptions.Development, PrivateApk));
        }
        [Test] public void NonResourceToolFoldersAreNotIncludedInPlayers()
        {
            AddPrototype("Editor/RefProto");
            Assert.IsEmpty(RefProtoBuildGuard.FindPrototypeResources(_root));
        }
    }
}

using System;
using System.IO;
using System.Collections.Generic;
using DemoRestaurant.Game.Online;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Online;
using NUnit.Framework;
using UnityEngine;

namespace DemoRestaurant.Render.Tests
{
    public sealed class CatalogArtResourcesTests
    {
        private string _directory;
        private AssetByteCache _cache;
        [SetUp] public void SetUp()
        {
            _directory=Path.GetFullPath("Logs/catalog-art-test-"+Guid.NewGuid().ToString("N"));
            _cache=new AssetByteCache(_directory);
        }
        [TearDown] public void TearDown()
        {
            RemoteAssetArt.Clear();
            string allowed=Path.GetFullPath("Logs")+Path.DirectorySeparatorChar;
            if(_directory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(_directory))Directory.Delete(_directory,true);
        }
        private JsonValue Manifest(params string[] paths)
        {
            var entries=new List<string>();
            foreach(string path in paths)
            {
                var bytes=File.ReadAllBytes("Assets/_Game/Resources/"+path+".png");
                string hash=AssetByteCache.Digest(bytes);_cache.Save(hash,bytes.Length,bytes);
                // PNG IHDR is big endian; texture decoding stays lazy until requested.
                int width=(bytes[16]<<24)|(bytes[17]<<16)|(bytes[18]<<8)|bytes[19];
                int height=(bytes[20]<<24)|(bytes[21]<<16)|(bytes[22]<<8)|bytes[23];
                entries.Add($"{{\"path\":\"{path}\",\"mediaId\":\"{hash}\",\"size\":{bytes.Length},\"width\":{width},\"height\":{height}}}");
            }
            return JsonValue.Parse("{\"revision\":1,\"files\":["+string.Join(",",entries)+"]}");
        }
        [Test] public void NativeStovePackagePreservesBackViewSkinAndCookingFrameMapWithoutEagerTextures()
        {
            var art=new ArtLibrary("Art/RefProto","Art/Concept");
            var manifest=Manifest("Art/RefProto/stove01","Art/RefProto/stove01_back","Art/RefProto/stove01_loop_sheet_00","Art/RefProto/stove01_cooking_loop_sheet_00");
            Assert.IsTrue(CatalogArtResources.Install(manifest,_cache));
            Assert.AreEqual(4,CatalogArtResources.ResourceCount);Assert.AreEqual(0,CatalogArtResources.LoadedTextureCount);
            var front=art.GetOptional("stove01");var back=art.GetOptional("stove01_back");
            Assert.AreNotSame(front,back);Assert.AreEqual(2,CatalogArtResources.LoadedTextureCount);
            Assert.IsNotNull(art.SkinOf("stove01"));Assert.Greater(art.SkinOf("stove01").tilePixelWidth,0);
            using(var idle=FurnitureLoopFrames.Load(art,"stove01"))
            using(var cooking=FurnitureLoopFrames.Load(art,"stove01","stove01_cooking"))
            {
                Assert.IsNotNull(idle);Assert.IsNotNull(cooking);Assert.AreEqual(7,idle.FrameCount);Assert.AreEqual(66,cooking.FrameCount);
                Assert.AreNotSame(cooking.At(0),cooking.At(.041f));
                Assert.AreSame(cooking.At(0),cooking.At(12f/25f)); // Native map returns to frame zero here.
                Assert.AreEqual(243,cooking.At(0).rect.width);Assert.AreEqual(211,cooking.At(0).rect.height);
            }
        }
        [Test] public void CorruptOrIncompletePackKeepsPriorCompletePackageAndClearRestoresBundledFallback()
        {
            var art=new ArtLibrary("Art/RefProto");var bundled=art.GetOptional("table01");
            var manifest=Manifest("Art/RefProto/table01");Assert.IsTrue(CatalogArtResources.Install(manifest,_cache));
            var remote=art.GetOptional("table01");Assert.AreNotSame(bundled,remote);
            var invalid=JsonValue.Parse("{\"revision\":2,\"files\":[{\"path\":\"Art/RefProto/table01\",\"mediaId\":\"bad\",\"size\":10,\"width\":2,\"height\":2}]}");
            Assert.IsFalse(CatalogArtResources.Install(invalid,_cache));Assert.AreSame(remote,art.GetOptional("table01"));
            RemoteAssetArt.Clear();Assert.AreSame(bundled,art.GetOptional("table01"));
        }
        [Test] public void WholeFloorPngUsesTheSameNativeDimensionsAndResourceLookup()
        {
            var manifest=Manifest("Art/RefProto/floor_wood");Assert.IsTrue(CatalogArtResources.Install(manifest,_cache));
            var art=new ArtLibrary("Art/RefProto");Assert.AreSame(CatalogArtResources.Texture("Art/RefProto/floor_wood"),art.GetOptional("floor_wood").texture);
        }
    }
}

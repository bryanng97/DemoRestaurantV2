using DemoRestaurant.Game.Online;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Online;
using NUnit.Framework;
using UnityEngine;
namespace DemoRestaurant.Render.Tests
{
    public sealed class RemoteAssetArtTests
    {
        [TearDown] public void TearDown(){RemoteAssetArt.Clear();}
        [Test] public void VerifiedSheetUsesTheSameRendererInShopAndWorldAndCanBeReplaced()
        {
            var texture=new Texture2D(2,1);texture.SetPixel(0,0,Color.red);texture.SetPixel(1,0,Color.green);texture.Apply();var bytes=texture.EncodeToPNG();Object.DestroyImmediate(texture);
            var data=JsonValue.Parse(@"{""mediaId"":""v1"",""width"":2,""height"":1,""render"":{""columns"":2,""rows"":1,""frames"":2,""fps"":2,""pivotX"":0.25,""pivotY"":0.5}}");
            data.Set("mediaId",JsonValue.Of(AssetByteCache.Digest(bytes))); Assert.IsTrue(RemoteAssetArt.Install("test",data,bytes));var key=RemoteAssetArt.Key("test");var art=new ArtLibrary();
            Assert.IsNotNull(art.GetOptional(key));using(var frames=FurnitureLoopFrames.Load(art,key)){Assert.IsNotNull(frames);Assert.AreEqual(2,frames.FrameCount);Assert.AreNotSame(frames.At(0),frames.At(.5f));Assert.AreEqual(Vector2.zero,frames.AnchorOffset);Assert.AreEqual(art.GetOptional(key).pivot,frames.At(0).pivot);}
            Assert.IsFalse(RemoteAssetArt.Install("test",data,bytes));RemoteAssetArt.Clear();Assert.IsNull(art.GetOptional(key));
        }
        [Test] public void InvalidRenderingMetadataDoesNotReplaceTheWorkingSprite()
        {
            var texture=new Texture2D(1,1);var bytes=texture.EncodeToPNG();Object.DestroyImmediate(texture);
            var valid=JsonValue.Parse(@"{""mediaId"":""good"",""width"":1,""height"":1,""render"":{""columns"":1,""rows"":1,""frames"":1,""fps"":0,""pivotX"":0.5,""pivotY"":0}}");
            valid.Set("mediaId",JsonValue.Of(AssetByteCache.Digest(bytes))); Assert.IsTrue(RemoteAssetArt.Install("test",valid,bytes));var before=RemoteAssetArt.Find(RemoteAssetArt.Key("test"));
            var invalid=JsonValue.Parse(@"{""mediaId"":""bad"",""render"":{""columns"":0}}");Assert.IsFalse(RemoteAssetArt.Install("test",invalid,bytes));Assert.AreSame(before,RemoteAssetArt.Find(RemoteAssetArt.Key("test")));
        }
        [Test] public void ANewRevisionUpdatesAnExistingArtLookupForTheSameFurniture()
        {
            var texture=new Texture2D(1,1);texture.SetPixel(0,0,Color.red);texture.Apply();var red=texture.EncodeToPNG();
            texture.SetPixel(0,0,Color.green);texture.Apply();var green=texture.EncodeToPNG();Object.DestroyImmediate(texture);
            var metadata=JsonValue.Parse(@"{""width"":1,""height"":1,""render"":{""columns"":1,""rows"":1,""frames"":1,""fps"":0,""pivotX"":0.5,""pivotY"":0}}");
            var art=new ArtLibrary();string key=RemoteAssetArt.Key("same_furniture");
            metadata.Set("mediaId",JsonValue.Of(AssetByteCache.Digest(red)));Assert.IsTrue(RemoteAssetArt.Install("same_furniture",metadata,red));
            var first=art.GetOptional(key);
            metadata.Set("mediaId",JsonValue.Of(AssetByteCache.Digest(green)));Assert.IsTrue(RemoteAssetArt.Install("same_furniture",metadata,green));
            Assert.AreNotSame(first,art.GetOptional(key));Assert.AreEqual(Color.green,art.GetOptional(key).texture.GetPixel(0,0));
        }
    }
}


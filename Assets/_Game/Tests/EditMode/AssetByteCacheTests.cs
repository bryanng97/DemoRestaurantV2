using System;
using System.IO;
using DemoRestaurant.Logic.Online;
using NUnit.Framework;
namespace DemoRestaurant.Logic.Tests
{
    public sealed class AssetByteCacheTests
    {
        private string _path;
        [SetUp] public void SetUp(){_path=Path.Combine(Path.GetTempPath(),"rc-media-"+Guid.NewGuid().ToString("N"));}
        [TearDown] public void TearDown(){if(Directory.Exists(_path))Directory.Delete(_path,true);}
        [Test] public void VerifiedBytesSurviveOfflineReloadAndFailedReplacement()
        {
            var bytes=new byte[]{1,2,3,4};string hash=AssetByteCache.Digest(bytes);var cache=new AssetByteCache(_path);
            cache.Save(hash,bytes.Length,bytes);CollectionAssert.AreEqual(bytes,new AssetByteCache(_path).Read(hash,bytes.Length));
            Assert.Throws<InvalidDataException>(()=>cache.Save(hash,4,new byte[]{4,3,2,1}));CollectionAssert.AreEqual(bytes,cache.Read(hash,4));
            Assert.IsNull(cache.Read(hash,3));Assert.Throws<ArgumentException>(()=>cache.Read("../escape",4));
        }
        [Test] public void CorruptionIsRejectedAndCanBeRepairedWithoutAcceptingUnverifiedBytes()
        {
            var bytes=new byte[]{1,2,3};string hash=AssetByteCache.Digest(bytes);var cache=new AssetByteCache(_path);cache.Save(hash,3,bytes);
            File.WriteAllBytes(Path.Combine(_path,hash+".png"),new byte[]{4,5,6});Assert.IsNull(cache.Read(hash,3));
            cache.Save(hash,3,bytes);CollectionAssert.AreEqual(bytes,cache.Read(hash,3));
        }
        [Test] public void PruningKeepsReferencedMediaAndRemovesAbandonedMedia()
        {
            var cache=new AssetByteCache(_path);var a=new byte[]{1}; var b=new byte[]{2};var ha=AssetByteCache.Digest(a);var hb=AssetByteCache.Digest(b);cache.Save(ha,1,a);cache.Save(hb,1,b);
            cache.Prune(new System.Collections.Generic.HashSet<string>{ha},1);Assert.IsNotNull(cache.Read(ha,1));Assert.IsNull(cache.Read(hb,1));
        }
    }
}


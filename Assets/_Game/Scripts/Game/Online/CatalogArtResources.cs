using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DemoRestaurant.Logic.Online;
using UnityEngine;
using UnityEngine.Networking;

namespace DemoRestaurant.Game.Online
{
    /// <summary>Verified native PNG resources, loaded as textures only when a renderer needs them.
    /// Existing content/skin metadata keeps its views, frame maps, geometry and state selection.</summary>
    public static class CatalogArtResources
    {
        private sealed class Resource
        {
            public string Hash; public int Size, Width, Height;
            public Texture2D Texture; public Sprite Sprite;
        }
        private static Dictionary<string, Resource> _files = new Dictionary<string, Resource>();
        private static AssetByteCache _cache;
        private const long TextureBudget = 64L*1024*1024;
        private static long _textureBytes;
        public static int Revision { get; private set; }
        public static string Status { get; private set; } = "Using included artwork";
        public static event Action StatusChanged;
        internal static void SetStatus(string value) {Status=value;StatusChanged?.Invoke();}
        public static int ResourceCount => _files.Count;
        public static int LoadedTextureCount { get { int count=0; foreach(var r in _files.Values) if(r.Texture!=null)count++;return count; } }
        public static bool Install(JsonValue manifest, AssetByteCache cache)
        {
            var next = new Dictionary<string, Resource>();
            try
            {
                foreach (var file in manifest["files"].Items)
                {
                    string path=file["path"].AsString(),hash=file["mediaId"].AsString(); int size=file["size"].AsInt();
                    if(!Regex.IsMatch(path??"",@"^Art/[A-Za-z0-9_-]+(/[A-Za-z0-9_-]+)+$") || next.ContainsKey(path) ||
                        file["width"].AsInt()<1||file["width"].AsInt()>2048||file["height"].AsInt()<1||file["height"].AsInt()>2048||
                        cache.Read(hash,size)==null) return false;
                    next.Add(path,new Resource {Hash=hash,Size=size,Width=file["width"].AsInt(),Height=file["height"].AsInt()});
                    if(next.Count>512)return false;
                }
            }
            catch(Exception){return false;}
            var previous=_files; _files=next; _cache=cache;_textureBytes=0;Revision=manifest["revision"].AsInt();
            SetStatus(Revision>0?$"Artwork ready for offline play ({next.Count} resources)":"Using included artwork");
            RemoteAssetArt.NotifyChanged();
            Destroy(previous); return true;
        }
        public static Texture2D Texture(string path)
        {
            if(!_files.TryGetValue(path,out var resource))return null;
            if(resource.Texture!=null)return resource.Texture;
            // Very large native multi-sheet loops must not eagerly exhaust mobile GPU memory.
            // The existing bundled compressed texture remains the fallback above this budget.
            long needed=(long)resource.Width*resource.Height*4;
            if(_textureBytes+needed>TextureBudget)return null;
            var bytes=_cache.Read(resource.Hash,resource.Size);if(bytes==null)return null;
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            // Release the CPU pixel copy; preserve full native sheet dimensions for frame rectangles.
            if(!texture.LoadImage(bytes,true)||texture.width!=resource.Width||texture.height!=resource.Height)
            {Destroy(texture);return null;}
            resource.Texture=texture;_textureBytes+=needed;return texture;
        }
        public static Sprite Sprite(string path)
        {
            if(!_files.TryGetValue(path,out var resource))return null;
            if(resource.Sprite!=null)return resource.Sprite;
            var texture=Texture(path);if(texture==null)return null;
            resource.Sprite=UnityEngine.Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,0),100,0,SpriteMeshType.FullRect);
            return resource.Sprite;
        }
        public static void Clear()
        {
            var previous=_files;_files=new Dictionary<string,Resource>();_cache=null;_textureBytes=0;Revision=0;Destroy(previous);
            SetStatus("Using included artwork");
        }
        private static void Destroy(Dictionary<string,Resource> resources)
        {foreach(var resource in resources.Values){Destroy(resource.Sprite);Destroy(resource.Texture);}}
        private static void Destroy(UnityEngine.Object value)
        {if(value!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}}
    }

    public sealed class CatalogArtPackLoader
    {
        private readonly AssetByteCache _cache;
        private readonly string _manifestPath,_host;
        private bool _cancelled,_busy;
        private int _revision=-1;
        public CatalogArtPackLoader(string root,string scope,string host)
        {
            _cache=new AssetByteCache(Path.Combine(root,"catalog-bytes"));_host=host;
            _manifestPath=Path.Combine(root,"catalog-"+AssetByteCache.Digest(System.Text.Encoding.UTF8.GetBytes(scope))+".json");
        }
        public void Cancel(){_cancelled=true;}
        public void LoadCached()
        {
            try{if(File.Exists(_manifestPath)){var data=JsonValue.Parse(File.ReadAllText(_manifestPath));if(CatalogArtResources.Install(data,_cache))_revision=data["revision"].AsInt();}}
            catch(Exception){/* Missing/corrupt cache preserves bundled art. */}
        }
        public async Awaitable Refresh(GameApiClient api)
        {
            if(_busy||_cancelled)return;_busy=true;
            try
            {
                var response=await api.GetCatalogArtPack();if(!response.Ok||_cancelled)return;
                var data=response.Data;if(data["revision"].AsInt()==_revision)return;
                int count=0,total=data["files"].Items.Count;var retained=new HashSet<string>();
                foreach(var file in data["files"].Items)
                {
                    if(++count>512)return;
                    CatalogArtResources.SetStatus($"Downloading artwork {count}/{total}");
                    string hash=file["mediaId"].AsString();int size=file["size"].AsInt();retained.Add(hash);
                    if(_cache.Read(hash,size)!=null)continue;
                    if(size<1||size>AssetByteCache.MaxBytes||!Uri.TryCreate(file["url"].AsString(),UriKind.Absolute,out var url)||url.Scheme!="https"||url.Host!=_host)return;
                    byte[] bytes=null;
                    for(int attempt=0;attempt<3&&bytes==null;attempt++)
                    {
                        using(var request=UnityWebRequest.Get(url))
                        {
                            var download=new LimitedDownload(size);request.downloadHandler=download;request.timeout=20;request.redirectLimit=0;
                            try{await request.SendWebRequest();}catch(Exception){}
                            if(_cancelled)return;
                            var candidate=download.Bytes;
                            if(request.result==UnityWebRequest.Result.Success&&candidate.Length==size&&AssetByteCache.Digest(candidate)==hash)bytes=candidate;
                        }
                        if(bytes==null&&attempt<2)await Awaitable.WaitForSecondsAsync(attempt+1);
                    }
                    if(bytes==null){CatalogArtResources.SetStatus("Artwork download paused; will retry online");return;}_cache.Save(hash,size,bytes);
                }
                if(_cancelled||!CatalogArtResources.Install(data,_cache))return;
                var temporary=_manifestPath+".tmp";File.WriteAllText(temporary,data.ToString());
                if(File.Exists(_manifestPath))File.Replace(temporary,_manifestPath,null);else File.Move(temporary,_manifestPath);
                _revision=data["revision"].AsInt();_cache.Prune(retained);
            }
            catch(Exception){/* Keep the previous complete package and retry the next refresh. */}
            finally{_busy=false;}
        }
        private sealed class LimitedDownload : DownloadHandlerScript
        {
            private readonly int _limit;private readonly MemoryStream _bytes=new MemoryStream();
            public LimitedDownload(int limit):base(new byte[16384]){_limit=limit;}
            public byte[] Bytes=>_bytes.ToArray();
            protected override bool ReceiveData(byte[] data,int length)
            {if(data==null||_bytes.Length+length>_limit)return false;_bytes.Write(data,0,length);return true;}
        }
    }
}

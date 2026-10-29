using System;
using System.Collections.Generic;
using System.IO;
using DemoRestaurant.Logic.Online;
using UnityEngine;
using UnityEngine.Networking;

namespace DemoRestaurant.Game.Online
{
    /// <summary>Verified remote furniture media shared by world and Shop renderers; bundled art remains the fallback.</summary>
    public static class RemoteAssetArt
    {
        public sealed class Entry
        {
            public string Hash;
            public Texture2D Texture;
            public Sprite Sprite;
            public int Columns, Rows, Frames;
            public float Fps;
            public Vector2 Pivot;
        }
        private static readonly Dictionary<string,Entry> Entries = new Dictionary<string,Entry>();
        public static event Action Changed;
        public static void NotifyChanged() => Changed?.Invoke();
        public static Entry Find(string key) => key != null && Entries.TryGetValue(key, out var value) ? value : null;
        public static string Key(string localId) => "remote:" + localId;
        public static bool Install(string localId, JsonValue data, byte[] bytes)
        {
            string key = Key(localId), hash=data["mediaId"].AsString();
            if (string.IsNullOrEmpty(localId) || bytes == null || bytes.Length > AssetByteCache.MaxBytes || AssetByteCache.Digest(bytes) != hash) return false;
            if(Find(key)?.Hash==hash) return false;
            var r=data["render"]; int columns=r["columns"].AsInt(),rows=r["rows"].AsInt(),frames=r["frames"].AsInt();
            float fps=(float)r["fps"].AsDouble();
            if(columns<1 || rows<1 || columns>16 || rows>16 || frames<1 || frames>128 || frames>columns*rows || fps<0 || fps>30 || frames>1&&fps==0) return false;
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!texture.LoadImage(bytes) || texture.width!=data["width"].AsInt() || texture.height!=data["height"].AsInt() || texture.width%columns!=0 || texture.height%rows!=0)
            { Destroy(texture); return false; }
            var pivot=new Vector2((float)r["pivotX"].AsDouble(),(float)r["pivotY"].AsDouble());
            if(pivot.x<0 || pivot.x>1 || pivot.y<0 || pivot.y>1) { Destroy(texture); return false; }
            var entry=new Entry{Hash=hash,Texture=texture,Columns=columns,Rows=rows,Frames=frames,Fps=fps,Pivot=pivot};
            entry.Sprite=Sprite.Create(texture,new Rect(0,texture.height-texture.height/rows,texture.width/columns,texture.height/rows),pivot,100,0,SpriteMeshType.FullRect);
            // Existing renderers can still reference the previous texture until their refresh event completes.
            Entries.TryGetValue(key,out var previous); Entries[key]=entry; Changed?.Invoke();
            if(previous!=null) { Destroy(previous.Sprite);Destroy(previous.Texture); }
            return true;
        }
        public static void Clear()
        {
            CatalogArtResources.Clear();
            var old=new List<Entry>(Entries.Values);Entries.Clear();Changed?.Invoke();
            foreach(var entry in old){Destroy(entry.Sprite);Destroy(entry.Texture);}
        }
        public static void Retain(System.Collections.Generic.ISet<string> keys)
        {
            var removed = new List<Entry>(); var names = new List<string>();
            foreach (var pair in Entries) if (!keys.Contains(pair.Key)) { names.Add(pair.Key); removed.Add(pair.Value); }
            foreach (var name in names) Entries.Remove(name);
            if (removed.Count > 0) Changed?.Invoke();
            foreach (var entry in removed) { Destroy(entry.Sprite); Destroy(entry.Texture); }
        }
        private static void Destroy(UnityEngine.Object value) { if(Application.isPlaying) UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value); }
    }

    public sealed class RemoteAssetLoader
    {
        private readonly AssetByteCache _cache;
        private readonly ServerIdMap _ids;
        private readonly string _manifestPath;
        private readonly string _host;
        private bool _busy;
        private bool _cancelled;
        public void Cancel() { _cancelled = true; }
        public RemoteAssetLoader(string root, string scope, string host, ServerIdMap ids)
        {
            _cache=new AssetByteCache(Path.Combine(root,"bytes"));_ids=ids;_host=host;
            _manifestPath=Path.Combine(root,"manifest-"+AssetByteCache.Digest(System.Text.Encoding.UTF8.GetBytes(scope))+".json");
        }
        public void LoadCached()
        {
            try { if(File.Exists(_manifestPath)) InstallCached(JsonValue.Parse(File.ReadAllText(_manifestPath))); }
            catch(Exception){ /* Corrupt/offline cache falls back to bundled art. */ }
        }
        private void InstallCached(JsonValue manifest)
        {
            foreach(var entry in manifest["entries"].Items)
            {
                var bytes=_cache.Read(entry["mediaId"].AsString(),entry["size"].AsInt());
                if(bytes!=null)RemoteAssetArt.Install(_ids.ToLocal(entry["assetId"].AsString()),entry,bytes);
            }
        }
        public async Awaitable Refresh(GameApiClient api)
        {
            if(_busy || _cancelled)return;_busy=true;
            try
            {
                var response=await api.GetAssetManifest(); if(!response.Ok || _cancelled)return;
                var manifest=response.Data;bool ready=true;
                var retained=new HashSet<string>();
                var retainedKeys=new HashSet<string>();
                foreach(var entry in manifest["entries"].Items)
                {
                    if (_cancelled) return;
                    string hash=entry["mediaId"].AsString();int size=entry["size"].AsInt();retained.Add(hash);
                    retainedKeys.Add(RemoteAssetArt.Key(_ids.ToLocal(entry["assetId"].AsString())));
                    byte[] bytes=_cache.Read(hash,size);
                    if(bytes==null)
                    {
                        string url=entry["url"].AsString();
                        if(size<1 || size>AssetByteCache.MaxBytes || !Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!=_host) { ready=false;continue; }
                        for(int attempt=0;attempt<3 && bytes==null;attempt++)
                        {
                            using(var request=UnityWebRequest.Get(url))
                            {
                                var download=new LimitedDownload(size);request.downloadHandler=download;request.timeout=20;request.redirectLimit=0;
                                try { await request.SendWebRequest(); } catch(Exception) { }
                                if (_cancelled) return;
                                var candidate=download.Bytes;
                                if(request.result==UnityWebRequest.Result.Success && candidate?.Length==size && AssetByteCache.Digest(candidate)==hash)
                                { _cache.Save(hash,size,candidate);bytes=candidate; }
                            }
                            if(bytes==null && attempt<2)await Awaitable.WaitForSecondsAsync(attempt+1);
                        }
                    }
                    if(bytes==null){ready=false;continue;}
                    RemoteAssetArt.Install(_ids.ToLocal(entry["assetId"].AsString()),entry,bytes);
                }
                // A failed replacement keeps the previous complete manifest and verified bytes for offline use.
                if(ready)
                {
                    RemoteAssetArt.Retain(retainedKeys);
                    var temp=_manifestPath+".tmp";File.WriteAllText(temp,manifest.ToString());
                    if(File.Exists(_manifestPath))File.Replace(temp,_manifestPath,null);else File.Move(temp,_manifestPath);
                    _cache.Prune(retained);
                }
            }
            catch(Exception){ /* Retry on the next online refresh; never interrupt restaurant play. */ }
            finally{_busy=false;}
        }
        private sealed class LimitedDownload : DownloadHandlerScript
        {
            private readonly int _limit;private readonly MemoryStream _bytes=new MemoryStream();
            public LimitedDownload(int limit):base(new byte[16384]){_limit=limit;}
            public byte[] Bytes=>_bytes.ToArray();
            protected override bool ReceiveData(byte[] data,int length)
            {if(data==null || _bytes.Length+length>_limit)return false;_bytes.Write(data,0,length);return true;}
        }
    }
}

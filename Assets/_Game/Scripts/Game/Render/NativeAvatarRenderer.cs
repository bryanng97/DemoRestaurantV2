using System;
using System.Collections.Generic;
using System.Linq;
using DemoRestaurant.Logic.Profile;
using UnityEngine;
using UnityEngine.Rendering;

namespace DemoRestaurant.Game.Render
{
    /// <summary>Composites the original avatar texture layers and samples the original model/animation.
    /// Rendered sprites are cached per look. The same frames serve preview, portrait and the restaurant.</summary>
    public sealed class NativeAvatarRenderer : IDisposable
    {
        private const string Folder="Art/RefProto/AvatarRuntime";
        private const int Width=256, Height=320, Ground=24;
        [Serializable] private sealed class Layer { public string name; public int x,y; }
        [Serializable] private sealed class Part { public string slot,id,texture,name; public string[] meshes; }
        [Serializable] private sealed class Motion { public string name; public float from,step; public int count; public string[] props; }
        [Serializable] private sealed class Manifest { public Layer[] layers; public Part[] parts; public Motion[] animations; }
        public sealed class LookImages
        {
            public Sprite Body,Back,Head;
            public CharacterFrames Frames;
            internal AvatarLook Look;
            internal Texture2D Composite;
            internal Material BodyMaterial,HairMaterial;
            internal readonly Dictionary<string,Sprite[]> Animations=new Dictionary<string,Sprite[]>();
            internal readonly Dictionary<string,Vector2> Hands=new Dictionary<string,Vector2>();
            internal readonly List<Sprite> OwnedSprites=new List<Sprite>();
        }
        private readonly AvatarCatalog _catalog;
        private readonly Manifest _manifest;
        private readonly Dictionary<string,Layer> _layers=new Dictionary<string,Layer>();
        private readonly Dictionary<string,Part> _parts=new Dictionary<string,Part>();
        private readonly Dictionary<string,Motion> _motions=new Dictionary<string,Motion>();
        private readonly Dictionary<string,Texture2D> _textures=new Dictionary<string,Texture2D>();
        private readonly Dictionary<string,LookImages> _cache=new Dictionary<string,LookImages>();
        private readonly Dictionary<string,int> _retained=new Dictionary<string,int>();
        private readonly List<string> _order=new List<string>();
        private readonly Dictionary<SkinnedMeshRenderer,Mesh> _meshes=new Dictionary<SkinnedMeshRenderer,Mesh>();
        private readonly Dictionary<string,Material> _props=new Dictionary<string,Material>();
        private readonly GameObject _model;
        private readonly Shader _shader;
        private readonly AnimationClip _clip;
        private GameObject _stage;
        private Camera _camera;
        private Renderer[] _renderers;
        public bool Available => _manifest!=null && _model!=null && _shader!=null && _clip!=null;

        public NativeAvatarRenderer(AvatarCatalog catalog)
        {
            _catalog=catalog;
            var text=Resources.Load<TextAsset>(Folder+"/native-avatar");
            if(text==null)return;
            _manifest=JsonUtility.FromJson<Manifest>(text.text);
            foreach(var layer in _manifest.layers)_layers[layer.name]=layer;
            foreach(var part in _manifest.parts)_parts[part.slot+":"+part.id]=part;
            foreach(var motion in _manifest.animations)_motions[motion.name]=motion;
            _model=Resources.Load<GameObject>(Folder+"/avatar");
            _shader=Resources.Load<Shader>(Folder+"/NativeAvatar");
            _clip=_model?.GetComponent<UnityEngine.Animation>()?.clip ?? Resources.LoadAll<AnimationClip>(Folder+"/avatar").Where(c=>!c.name.StartsWith("__preview")).OrderByDescending(c=>c.length).FirstOrDefault();
        }

        private static void Destroy(UnityEngine.Object obj)
        {
            if(obj==null)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj);
        }
        private Material Material()
        {
            var material=new Material(_shader);
            material.SetInt("_NativeDepthTest",(int)(SystemInfo.usesReversedZBuffer?CompareFunction.GreaterEqual:CompareFunction.LessEqual));
            return material;
        }
        private Part Resolve(AvatarLook look,AvatarSlot slot) =>
            _parts.TryGetValue(slot+":"+look[slot],out var part)?part:null;
        public string PartName(AvatarPartDef part) => part!=null && _parts.TryGetValue(part.OwnershipKey,out var native)?native.name:null;
        private Color ColorOf(AvatarLook look,AvatarSlot slot,Color fallback)
        {
            var part=_catalog.Find(slot,look[slot]);
            return part!=null && ColorUtility.TryParseHtmlString(part.Color,out var color)?color:fallback;
        }
        private Texture2D Texture(string name)
        {
            if(!_textures.TryGetValue(name,out var texture))_textures[name]=texture=Resources.Load<Texture2D>(Folder+"/Layers/"+name);
            return texture;
        }
        private void Paint(Color[] pixels,string name,Color? tint=null,bool multiply=false)
        {
            if(string.IsNullOrEmpty(name)||name=="null")return;
            if(!_layers.TryGetValue(name,out var layer)||Texture(name)==null)throw new InvalidOperationException("Missing native avatar layer: "+name);
            var texture=Texture(name);var source=texture.GetPixels();
            for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
            {
                int tx=layer.x+x,ty=layer.y+texture.height-1-y;
                if(tx<0||ty<0||tx>=512||ty>=512)continue;
                int index=(511-ty)*512+tx;var c=source[y*texture.width+x];
                if(tint.HasValue){var t=tint.Value;float shade=Mathf.Max(c.r,Mathf.Max(c.g,c.b));c=new Color(t.r*shade,t.g*shade,t.b*shade,c.a);}
                if(multiply){var k=Color.Lerp(Color.white,c,c.a);pixels[index]=new Color(pixels[index].r*k.r,pixels[index].g*k.g,pixels[index].b*k.b,1);}
                else pixels[index]=Color.Lerp(pixels[index],new Color(c.r,c.g,c.b,1),c.a);
            }
        }
        private Texture2D Composite(AvatarLook look)
        {
            var pixels=Enumerable.Repeat(ColorOf(look,AvatarSlot.SkinTone,new Color(1,.86f,.75f)),512*512).ToArray();
            Paint(pixels,Resolve(look,AvatarSlot.Pants)?.texture,ColorOf(look,AvatarSlot.PantsColor,Color.white));
            Paint(pixels,Resolve(look,AvatarSlot.Shirt)?.texture,ColorOf(look,AvatarSlot.ShirtColor,Color.white));
            Paint(pixels,Resolve(look,AvatarSlot.Hat)?.texture);
            Paint(pixels,Resolve(look,AvatarSlot.Eyebrows)?.texture);
            Paint(pixels,Resolve(look,AvatarSlot.Eyes)?.texture);
            Paint(pixels,Resolve(look,AvatarSlot.Gender)?.texture);
            Paint(pixels,"TextureShading",multiply:true);
            var texture=new Texture2D(512,512,TextureFormat.RGBA32,false){name="NativeAvatarComposite"};
            texture.SetPixels(pixels);texture.Apply(false,true);return texture;
        }
        private void EnsureStage()
        {
            if(_stage!=null)return;
            _stage=UnityEngine.Object.Instantiate(_model);_stage.name="NativeAvatarRenderStage";
            _stage.hideFlags=HideFlags.HideAndDontSave;_stage.transform.position=new Vector3(0,-1000000,0);
            foreach(var animation in _stage.GetComponentsInChildren<Animation>(true))animation.enabled=false;
            _renderers=_stage.GetComponentsInChildren<Renderer>(true);
            foreach(var renderer in _renderers)
            {
                renderer.gameObject.layer=30;
                if(renderer is SkinnedMeshRenderer skinned){skinned.updateWhenOffscreen=true;_meshes[skinned]=new Mesh();}
            }
            var cameraObject=new GameObject("NativeAvatarMatrixCamera"){hideFlags=HideFlags.HideAndDontSave};
            _camera=cameraObject.AddComponent<Camera>();_camera.enabled=false;
            _camera.orthographic=true;_camera.nearClipPlane=1;_camera.farClipPlane=100000;_camera.cullingMask=0;
        }
        private void Configure(LookImages images,string[] props)
        {
            EnsureStage();_stage.SetActive(true);
            var visible=new HashSet<string>(new[]{"face0"});
            foreach(var slot in new[]{AvatarSlot.Shirt,AvatarSlot.Pants,AvatarSlot.HairStyle,AvatarSlot.Hat})
                foreach(var mesh in Resolve(images.Look,slot)?.meshes??Array.Empty<string>())visible.Add(mesh);
            foreach(var prop in props??Array.Empty<string>())visible.Add(prop);
            foreach(var renderer in _renderers)
            {
                string name=renderer.name;renderer.gameObject.SetActive(visible.Contains(name));
                if(name.StartsWith("hair"))renderer.sharedMaterial=images.HairMaterial;
                else if(name=="brush"||name=="bucket"||name=="bucket_PIVOT"||name=="repair")
                {
                    string key=name=="repair"?"Repair":"Cleaner";
                    if(!_props.TryGetValue(key,out var material)){material=Material();material.mainTexture=Texture(key);_props[key]=material;}
                    renderer.sharedMaterial=material;
                }
                else renderer.sharedMaterial=images.BodyMaterial;
            }
        }
        private Sprite RenderPose(LookImages entry,float time,bool back,bool portrait,out Vector2 hand,out float inkHeight)
        {
            _clip.SampleAnimation(_stage,time);
            int w=Width,h=portrait?Width:Height;
            var rotation=Quaternion.Euler(portrait?8:30,portrait?200:back?315:225,0);
            _camera.transform.rotation=rotation;_camera.aspect=(float)w/h;
            _camera.orthographicSize=portrait?3400:h*.5f*30;
            Vector3 focus=_stage.transform.position+_camera.transform.up*((h*.5f-Ground)*30);
            if(portrait)focus=_stage.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Bip01_Head").position+Vector3.up*1900;
            _camera.transform.position=focus-rotation*Vector3.forward*30000;
            var handTransform=_stage.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Bip01_R_Hand");
            hand=_camera.WorldToViewportPoint(handTransform.position);
            var target=RenderTexture.GetTemporary(w,h,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var previous=RenderTexture.active;var commands=new CommandBuffer{name="Native avatar pose"};
            Texture2D texture=null;
            try
            {
                commands.SetRenderTarget(target);commands.ClearRenderTarget(true,true,Color.clear,SystemInfo.usesReversedZBuffer?0:1);
                // This is an explicit command-buffer draw, not Camera.Render's target-flipping path.
                commands.SetViewProjectionMatrices(_camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(_camera.projectionMatrix,false));
                foreach(var renderer in _renderers)
                {
                    if(!renderer.gameObject.activeInHierarchy)continue;
                    Mesh mesh;
                    if(renderer is SkinnedMeshRenderer skinned){mesh=_meshes[skinned];skinned.BakeMesh(mesh);}
                    else {var filter=renderer.GetComponent<MeshFilter>();if(filter==null)continue;mesh=filter.sharedMesh;}
                    for(int s=0;s<mesh.subMeshCount;s++)commands.DrawMesh(mesh,renderer.localToWorldMatrix,renderer.sharedMaterial,s,0);
                }
                Graphics.ExecuteCommandBuffer(commands);RenderTexture.active=target;
                texture=new Texture2D(w,h,TextureFormat.RGBA32,false){name="native_avatar_"+(portrait?"portrait":back?"back":"front")};
                texture.ReadPixels(new Rect(0,0,w,h),0,0);texture.Apply();
                var pixels=texture.GetPixels32();int min=h,max=-1;
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[y*w+x].a>16){min=Math.Min(min,y);max=Math.Max(max,y);}
                if(max<min)throw new InvalidOperationException("Native avatar render is empty.");
                inkHeight=(max-min+1)/100f;
                var sprite=Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(.5f,portrait?.5f:Ground/(float)h),100,0,SpriteMeshType.Tight);
                texture.Apply(false,true);entry.OwnedSprites.Add(sprite);return sprite;
            }
            catch {Destroy(texture);throw;}
            finally {commands.Dispose();RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
        public LookImages Get(AvatarLook look)
        {
            if(_cache.TryGetValue(look.Key,out var entry))return entry;
            entry=new LookImages{Look=look.Clone(),Composite=Composite(look)};
            entry.BodyMaterial=Material();entry.BodyMaterial.mainTexture=entry.Composite;
            entry.HairMaterial=Material();entry.HairMaterial.color=ColorOf(look,AvatarSlot.HairColor,new Color(.65f,.45f,.25f));
            try
            {
                Configure(entry,null);
                entry.Body=RenderPose(entry,4.4f,false,false,out _,out float height);
                entry.Back=RenderPose(entry,4.4f,true,false,out _,out _);
                entry.Head=RenderPose(entry,4.4f,false,true,out _,out _);
                entry.Frames=CharacterFrames.CreateDynamic(height,new Vector2(.5f,Ground/(float)Height),
                    (anim,back)=>Animation(entry,anim,back),(anim,back)=>Hand(entry,anim,back));
                _cache[look.Key]=entry;_order.Add(look.Key);
                if(_order.Count>12)
                {
                    string old=_order.FirstOrDefault(k=>k!=look.Key&&!_retained.ContainsKey(k));
                    if(old!=null){DisposeLook(_cache[old]);_cache.Remove(old);_order.Remove(old);}
                }
                return entry;
            }
            catch {DisposeLook(entry);throw;}
            finally {if(_stage!=null)_stage.SetActive(false);}
        }
        private Sprite[] Animation(LookImages entry,string name,bool back)
        {
            string key=name+(back?"_b":"_f");
            if(entry.Animations.TryGetValue(key,out var frames))return frames;
            var motion=_motions[name];frames=new Sprite[Math.Max(1,motion.count)];
            try
            {
                Configure(entry,motion.props);
                for(int i=0;i<frames.Length;i++)
                {
                    frames[i]=RenderPose(entry,motion.from+i*motion.step,back,false,out var hand,out _);
                    frames[i].name="native_avatar_"+key+"_"+i.ToString("D2");
                    if(i==0)entry.Hands[key]=hand;
                }
                entry.Animations[key]=frames;return frames;
            }
            finally {if(_stage!=null)_stage.SetActive(false);}
        }
        private Vector2? Hand(LookImages entry,string name,bool back)
        {
            Animation(entry,name,back);
            return entry.Hands[name+(back?"_b":"_f")];
        }
        public void Retain(AvatarLook look){Get(look);_retained[look.Key]=(_retained.TryGetValue(look.Key,out int n)?n:0)+1;}
        public void Release(string key){if(key!=null&&_retained.TryGetValue(key,out int n)){if(n<=1)_retained.Remove(key);else _retained[key]=n-1;}}
        private static void DisposeLook(LookImages entry)
        {
            foreach(var sprite in entry.OwnedSprites){Destroy(sprite.texture);Destroy(sprite);}
            Destroy(entry.BodyMaterial);Destroy(entry.HairMaterial);Destroy(entry.Composite);
        }
        public void Dispose()
        {
            foreach(var entry in _cache.Values)DisposeLook(entry);_cache.Clear();_order.Clear();_retained.Clear();
            foreach(var mesh in _meshes.Values)Destroy(mesh);_meshes.Clear();
            foreach(var material in _props.Values)Destroy(material);_props.Clear();
            Destroy(_stage);if(_camera!=null)Destroy(_camera.gameObject);
        }
    }
}

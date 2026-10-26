using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DemoRestaurant.Render.Tests
{
    public sealed class FurnitureAnimationTests
    {
        private static object Call(object target, string method, params object[] args) => target.GetType()
            .GetMethod(method, BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
        private static object Field(object target,string name) => target.GetType()
            .GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(target);

        [Test]
        public void NativeLoopMetadataPreservesFullSourceTimingAndValidFrameReferences()
        {
            var content=ContentLoader.Load("malaysia");var art=ArtLibrary.For(content.Theme);
            var manifest=art.SkinOf("ref_lucky_cat_decor");
            if(manifest==null)Assert.Ignore("Private animation pack is not installed.");
            var loops=manifest.ByKey.Values.Where(h=>h.animationFrames>0).ToArray();
            Assert.Greater(loops.Length,0);
            foreach(var hint in loops)
            {
                Assert.Greater(hint.animationFps,0,hint.key);
                Assert.Greater(hint.animationColumns,0,hint.key);Assert.Greater(hint.animationRows,0,hint.key);
                Assert.Greater(hint.animationWidth,0,hint.key);Assert.Greater(hint.animationHeight,0,hint.key);
                Assert.IsTrue(hint.frameMap.All(i=>i>=0 && i<hint.animationFrames),hint.key);
                Assert.GreaterOrEqual(hint.frameMap.Length,hint.animationFrames,hint.key);
                Assert.IsNotNull(art.GetOptional(string.IsNullOrEmpty(hint.animationOwner) ? hint.key.Substring(0,hint.key.Length-5) : hint.animationOwner),"Loop must belong to an installed base sprite.");
            }
            Assert.IsNull(FurnitureLoopFrames.Load(new ArtLibrary("Art/Concept"),"plant01"),"A static replacement skin must not inherit another skin's loops.");
        }

        [UnityTest]
        public IEnumerator FurnitureLoopsPlayWithoutDrift_AfterRotationAndInDecorationMode()
        {
            yield return new EnterPlayMode();
            var content=ContentLoader.Load("malaysia");var art=ArtLibrary.For(content.Theme);
            var def=content.Pack.Furniture.FirstOrDefault(f=>f.Id=="ref_lucky_cat_decor");
            if(def==null || art.HintsFor("ref_lucky_cat_decor_loop")==null)Assert.Ignore("Private animation pack is not installed.");
            foreach(Facing facing in Enum.GetValues(typeof(Facing)))
            {
                var root=new GameObject("FurnitureLoopTest");
                try
                {
                    var room=new RestaurantLayout(10,10,new GridPos(0,7));
                    room.TryPlace(def,new GridPos(4,4),facing,out var piece);
                    if(facing==Facing.SouthWest)
                    {
                        room.TryPlace(content.Pack.GetFurniture("ref_water_fountain"),new GridPos(6,4),Facing.SouthWest,out _);
                        room.TryPlace(content.Pack.GetFurniture("ref_fish_tank_small"),new GridPos(2,4),Facing.SouthWest,out _);
                    }
                    var sim=new RestaurantSim(room,new RestaurantMenu(content.Pack.Dishes,1),1);
                    var view=root.AddComponent<RestaurantView>();view.Bind(sim,content,Array.Empty<int>());
                    var renderer=(SpriteRenderer)((IDictionary)Field(view,"_furniture"))[piece];
                    string key=FacingUtil.IsBackView(facing) ? def.Id+"_back" : def.Id;
                    var hint=art.HintsFor(key+"_loop");
                    using(var frames=FurnitureLoopFrames.Load(art,key))
                    {
                        Assert.IsNotNull(frames);Assert.Greater(frames.FrameCount,1);
                        int change=Array.FindIndex(hint.frameMap,i=>i!=hint.frameMap[0]);
                        float time=(change+.25f)/hint.animationFps;
                        Call(view,"AnimateFurniture",0f);var first=renderer.sprite;var position=renderer.transform.localPosition;
                        var scale=renderer.transform.localScale;bool mirror=renderer.flipX;
                        Call(view,"AnimateFurniture",time);
                        Assert.AreNotSame(first,renderer.sprite,"Native frames must actually advance.");
                        Assert.AreEqual(position,renderer.transform.localPosition,"Animating art must not move its blocked cell.");
                        Assert.AreEqual(scale,renderer.transform.localScale);Assert.AreEqual(mirror,renderer.flipX);
                        Assert.AreSame(frames.At(time),frames.At(time+hint.frameMap.Length/hint.animationFps));
                        if(facing==Facing.SouthWest) CaptureLoop(root,view);
                        view.ShowLayout(room,true);
                        renderer=(SpriteRenderer)((IDictionary)Field(view,"_furniture"))[piece];
                        Call(view,"AnimateFurniture",time);Assert.IsTrue(renderer.sprite.name.Contains("_loop_"));
                        var preview=new GameObject("Preview").AddComponent<SpriteRenderer>();preview.transform.SetParent(root.transform,false);
                        view.DrawFurniturePreview(preview,def,new GridPos(4,4),facing);
                        view.AnimateFurniturePreview(preview,def,facing,time);
                        Assert.AreEqual(renderer.sprite,preview.sprite,"Held and placed furniture use the same native animation.");
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            yield return new ExitPlayMode();
        }

        private static void CaptureLoop(GameObject root,RestaurantView view)
        {
            var cameraRoot=new GameObject("FurnitureProofCamera");var camera=cameraRoot.AddComponent<Camera>();
            var texture=new RenderTexture(900,600,24);var image=new Texture2D(900,600,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;var children=root.GetComponentsInChildren<Transform>(true);
            var layers=children.Select(t=>t.gameObject.layer).ToArray();
            try
            {
                foreach(var child in children) child.gameObject.layer=31;
                camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=2.2f;
                camera.transform.position=new Vector3(0,-1.65f,-10);camera.backgroundColor=new Color(.57f,.73f,.28f);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=texture;
                Directory.CreateDirectory("Logs/FurnitureAnimation");
                for(int i=0;i<48;i++)
                {
                    Call(view,"AnimateFurniture",i/12f);Call(view,"SortDepth");camera.Render();RenderTexture.active=texture;
                    image.ReadPixels(new Rect(0,0,900,600),0,0);image.Apply();
                    File.WriteAllBytes($"Logs/FurnitureAnimation/{i:D3}.png",image.EncodeToPNG());
                }
            }
            finally
            {
                for(int i=0;i<children.Length;i++)children[i].gameObject.layer=layers[i];
                RenderTexture.active=previous;camera.targetTexture=null;
                UnityEngine.Object.DestroyImmediate(cameraRoot);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(image);
            }
        }

        [UnityTest]
        public IEnumerator ToiletUsesSittingPose_ThenHandWashingPlaysTheReferenceHandMotion()
        {
            yield return new EnterPlayMode();
            var root=new GameObject("BathroomAnimationTest");
            try
            {
                var content=ContentLoader.Load("malaysia");
                var toilet=content.Pack.Furniture.FirstOrDefault(f=>f.Kind==FurnitureKind.Toilet);
                var sink=content.Pack.Furniture.FirstOrDefault(f=>f.Kind==FurnitureKind.Sink);
                if(toilet==null || sink==null)Assert.Ignore("Bathroom furniture is not installed in this content pack.");
                var room=new RestaurantLayout(10,10,new GridPos(0,8));
                room.TryPlace(content.Pack.GetFurniture("gas_stove"),new GridPos(3,0),Facing.SouthWest,out _);
                room.TryPlace(content.Pack.GetFurniture("dining_table"),new GridPos(4,5),Facing.SouthWest,out _);
                room.TryPlace(content.Pack.GetFurniture("wooden_chair"),new GridPos(4,6),Facing.NorthEast,out _);
                room.TryPlace(toilet,new GridPos(8,0),Facing.SouthWest,out _);
                room.TryPlace(sink,new GridPos(6,0),Facing.SouthWest,out _);
                var sim=new RestaurantSim(room,new RestaurantMenu(new[]{content.Pack.Dishes[0]},1),3,
                    new SimSettings{ToiletChance=1,ToiletSeconds=3,SinkSeconds=3});
                sim.Hire(content.Pack.StaffTypes.First(s=>s.Role==StaffRole.Chef),new GridPos(3,1));
                sim.Hire(content.Pack.StaffTypes.First(s=>s.Role==StaffRole.Waiter),new GridPos(3,3));
                var type=content.Pack.CustomerTypes[0];
                var guest=sim.SpawnCustomer(new CustomerTypeDef(type.Id,type.NameKey,4,9999,1,foodPatienceSeconds:9999));
                var view=root.AddComponent<RestaurantView>();view.Bind(sim,content,Array.Empty<int>());
                void Until(CustomerState state){for(int i=0;i<20000 && guest.State!=state;i++)sim.Tick(.05f);Assert.AreEqual(state,guest.State);Call(view,"LateUpdate");}
                Until(CustomerState.OnToilet);
                sim.Tick(.5f);Call(view,"LateUpdate");
                var pose=Call(view,"PoseFor",guest);
                Assert.IsTrue((bool)Field(pose,"Seated"));Assert.IsFalse((bool)Field(pose,"Cooking"));
                Assert.AreEqual("ui.activity.toilet",Field(pose,"ActivityKey"));
                Assert.Greater((float)Field(pose,"Progress"),0);
                var character=((IDictionary)Field(view,"_characters"))[guest];
                var status=(UnityEngine.UI.Text)Field(character,"_activityText");
                Assert.IsTrue(status.gameObject.activeInHierarchy);StringAssert.Contains(Loc.Get("ui.activity.toilet"),status.text);
                Canvas.ForceUpdateCanvases();yield return null;
                CaptureActivity(root,new Vector2(3,-1.2f),"bathroom-toilet-status.png");
                var body=(SpriteRenderer)Field(character,"_body");
                if(!body.sprite.name.Contains("_sit_"))Assert.Ignore("Native character frames are not installed.");
                Until(CustomerState.UsingSink);pose=Call(view,"PoseFor",guest);
                Assert.IsFalse((bool)Field(pose,"Seated"));Assert.IsTrue((bool)Field(pose,"Cooking"));
                Assert.AreEqual("ui.activity.washing",Field(pose,"ActivityKey"));
                sim.Tick(.6f);Call(view,"LateUpdate");
                StringAssert.Contains(Loc.Get("ui.activity.washing"),status.text);StringAssert.Contains("%",status.text);
                Assert.IsTrue(body.sprite.name.Contains("_cook_"),body.sprite.name);
                Canvas.ForceUpdateCanvases();yield return null;
                CaptureActivity(root,new Vector2(3,-1.2f),"bathroom-wash-status.png");
                Call(character,"SyncFrames",guest,pose,0f,FacingUtil.IsMirrored(guest.Facing),FacingUtil.IsBackView(guest.Facing));
                var before=body.sprite;
                Call(character,"SyncFrames",guest,pose,.11f,FacingUtil.IsMirrored(guest.Facing),FacingUtil.IsBackView(guest.Facing));
                Assert.AreNotSame(before,body.sprite,"Washing must move the hands rather than just stand at the sink.");
                Until(CustomerState.Left);Call(view,"LateUpdate");Assert.IsFalse(status.gameObject.activeInHierarchy);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            yield return new ExitPlayMode();
        }

        internal static void CaptureActivity(GameObject root,Vector2 centre,string filename)
        {
            var cameraRoot=new GameObject("ServiceProofCamera");var camera=cameraRoot.AddComponent<Camera>();
            var target=new RenderTexture(1000,700,24);var image=new Texture2D(1000,700,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;var children=root.GetComponentsInChildren<Transform>(true);
            var layers=children.Select(t=>t.gameObject.layer).ToArray();
            try
            {
                foreach(var child in children)child.gameObject.layer=31;
                camera.orthographic=true;camera.orthographicSize=1.8f;camera.cullingMask=1<<31;
                camera.transform.position=new Vector3(centre.x,centre.y,-10);camera.backgroundColor=new Color(.57f,.73f,.28f);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=target;Canvas.ForceUpdateCanvases();camera.Render();
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1000,700),0,0);image.Apply();
                Directory.CreateDirectory("Logs");File.WriteAllBytes(Path.Combine("Logs",filename),image.EncodeToPNG());
            }
            finally
            {
                for(int i=0;i<children.Length;i++)children[i].gameObject.layer=layers[i];
                RenderTexture.active=previous;camera.targetTexture=null;
                UnityEngine.Object.DestroyImmediate(cameraRoot);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}

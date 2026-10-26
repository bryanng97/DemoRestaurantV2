using System;
using System.IO;
using System.Linq;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace DemoRestaurant.Render.Tests
{
    public sealed class StoveViewTests
    {
        [Test]
        public void AllInstalledNativeStoves_HaveDistinctFrontAndBackAssets()
        {
            var content=ContentLoader.Load("malaysia");var art=ArtLibrary.For(content.Theme);
            var stoves=content.Pack.Furniture.Where(f=>f.Kind==FurnitureKind.Stove && f.Id.StartsWith("ref_stove")).ToArray();
            if(stoves.Length==0)Assert.Ignore("Private stove pack is not installed.");
            foreach(var stove in stoves)
            {
                var visual=content.ArtForFurniture(stove.Id);
                Assert.IsNotEmpty(visual.SpriteBack,stove.Id);
                Assert.IsNotNull(art.GetOptional(visual.SpriteBack),stove.Id);
                Assert.AreNotSame(art.Get(visual.Sprite),art.Get(visual.SpriteBack),stove.Id);
                using(var idle=FurnitureLoopFrames.Load(art,visual.SpriteBack))
                using(var cooking=FurnitureLoopFrames.Load(art,visual.SpriteBack,visual.SpriteBack+"_cooking"))
                using(var done=FurnitureLoopFrames.Load(art,visual.SpriteBack,visual.SpriteBack+"_done"))
                { if(stove.Id=="ref_stove13") { Assert.IsNotNull(idle);Assert.IsNotNull(cooking);Assert.IsNotNull(done); } }
            }
        }

        [TestCase("ref_stove13",Facing.SouthWest)]
        [TestCase("ref_stove13",Facing.SouthEast)]
        [TestCase("ref_stove13",Facing.NorthEast)]
        [TestCase("ref_stove13",Facing.NorthWest)]
        [TestCase("ref_stove03",Facing.SouthWest)]
        [TestCase("ref_stove03",Facing.SouthEast)]
        [TestCase("ref_stove03",Facing.NorthEast)]
        [TestCase("ref_stove03",Facing.NorthWest)]
        public void StoveBodyAndWorkMat_FollowFacingAndKeepChefOnTheMat(string id,Facing facing)
        {
            var content=ContentLoader.Load("malaysia");if(!content.Pack.HasFurniture(id))Assert.Ignore("Private stove pack is not installed.");
            var def=content.Pack.GetFurniture(id);var origin=new GridPos(4,4);var room=new RestaurantLayout(10,10,new GridPos(0,9));
            room.TryPlace(def,origin,facing,out var piece);
            var sim=new RestaurantSim(room,new RestaurantMenu(content.Pack.Dishes,1),1);
            var chef=sim.Hire(content.Pack.StaffTypes.First(t=>t.Role==StaffRole.Chef && !t.IsOwner),new GridPos(2,8));sim.Tick(.1f);
            var front=FurnitureWorkArea.FrontTile(def,origin,facing);
            Assert.AreEqual(front,chef.Position.Cell);Assert.IsTrue(room.Grid.IsWalkable(front));Assert.IsFalse(room.Grid.IsWalkable(origin));
            var root=new GameObject("StoveProof");
            try
            {
                var view=root.AddComponent<RestaurantView>();view.Bind(sim,content,Array.Empty<int>());
                var renderer=root.transform.Find(id+"#"+piece.InstanceId).GetComponent<SpriteRenderer>();
                var art=ArtLibrary.For(content.Theme);var visual=content.ArtForFurniture(id);
                Assert.AreSame(art.Get(FacingUtil.IsBackView(facing)?visual.SpriteBack:visual.Sprite),renderer.sprite);
                if(FacingUtil.IsBackView(facing))
                {
                    var mat=renderer.transform.Find("WorkMat");Assert.IsNotNull(mat);
                    Assert.Less(Vector3.Distance(mat.position,root.transform.TransformPoint(IsoProjection.GridToWorld(front))),.001f);
                }
                foreach(var cell in new[]{origin,front})
                {
                    var go=new GameObject(cell==origin?"BodyFootprint":"WorkingFootprint");go.transform.SetParent(root.transform,false);
                    var overlay=go.AddComponent<SpriteRenderer>();var color=cell==origin?new Color(.3f,1,.4f,.8f):new Color(.2f,.75f,1,.8f);
                    overlay.sprite=PlaceholderArt.Floor(1,1,color,color);overlay.transform.localPosition=IsoProjection.GridToWorld(cell);overlay.sortingOrder=renderer.sortingOrder-1;
                }
                Capture(root,origin,id+"-"+facing);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Capture(GameObject root,GridPos origin,string name)
        {
            var go=new GameObject("StoveProofCamera");var camera=go.AddComponent<Camera>();var target=new RenderTexture(800,600,24);
            var pixels=new Texture2D(800,600,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            var children=root.GetComponentsInChildren<Transform>(true);var layers=children.Select(t=>t.gameObject.layer).ToArray();
            try
            {
                foreach(var child in children)child.gameObject.layer=31;
                var centre=IsoProjection.GridToWorld(origin.X+.5f,origin.Y+.5f);
                camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=1.4f;camera.transform.position=new Vector3(centre.x,centre.y+.3f,-10);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.55f,.75f,.63f);camera.targetTexture=target;
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,800,600),0,0);pixels.Apply();
                Directory.CreateDirectory("Logs");File.WriteAllBytes("Logs/stove-"+name+".png",pixels.EncodeToPNG());
            }
            finally
            {
                for(int i=0;i<children.Length;i++)children[i].gameObject.layer=layers[i];
                RenderTexture.active=previous;camera.targetTexture=null;
                UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}

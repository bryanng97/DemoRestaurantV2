using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace DemoRestaurant.Render.Tests
{
    public sealed class MachineFootprintTests
    {
        [TestCase(Facing.SouthWest)]
        [TestCase(Facing.SouthEast)]
        [TestCase(Facing.NorthEast)]
        [TestCase(Facing.NorthWest)]
        public void NativePinball_DepthFootprintMatchesPreviewAndGuestAccess(Facing facing)
        {
            var content = ContentLoader.Load("malaysia");
            if (!content.Pack.HasFurniture("ref_pinball_machine")) Assert.Ignore("Private catalog is not installed.");
            var def = content.Pack.GetFurniture("ref_pinball_machine");
            Assert.AreEqual(1,def.SizeX);Assert.AreEqual(2,def.SizeY);
            bool rotated=facing==Facing.SouthEast || facing==Facing.NorthWest;
            var origin=new GridPos(4,4);
            var room=new RestaurantLayout(10,10,new GridPos(0,9));
            Assert.IsTrue(room.TryPlace(def,origin,facing,out var item));
            Assert.AreEqual(rotated?2:1,item.SizeX);Assert.AreEqual(rotated?1:2,item.SizeY);
            for(int y=0;y<10;y++) for(int x=0;x<10;x++)
                Assert.AreEqual(!item.Covers(new GridPos(x,y)),room.Grid.IsWalkable(new GridPos(x,y)));
            var sim=new RestaurantSim(room,new RestaurantMenu(content.Pack.Dishes,1),7,new SimSettings{EntertainmentSeconds=30});
            var customer=sim.SpawnCustomer(content.Pack.CustomerTypes[0]);
            for(float t=0;t<30 && customer.State!=CustomerState.PlayingEntertainment;t+=.05f)sim.Tick(.05f);
            Assert.AreEqual(CustomerState.PlayingEntertainment,customer.State);
            var stand=customer.Position.Cell;
            Assert.IsFalse(item.Covers(stand));Assert.IsTrue(item.Covers(stand-FacingUtil.ToDelta(facing)));
            var root=new GameObject("MachineFootprintProof");
            try
            {
                var view=root.AddComponent<RestaurantView>();view.Bind(sim,content,Array.Empty<int>());
                var renderer=root.transform.Find(def.Id+"#"+item.InstanceId).GetComponent<SpriteRenderer>();
                var preview=new GameObject("HeldPreview").AddComponent<SpriteRenderer>();preview.transform.SetParent(root.transform,false);
                view.DrawFurniturePreview(preview,def,origin,facing);
                Assert.AreSame(renderer.sprite,preview.sprite);Assert.AreEqual(renderer.flipX,preview.flipX);
                Assert.AreEqual(renderer.transform.localPosition,preview.transform.localPosition);
                Assert.AreEqual(renderer.transform.localScale,preview.transform.localScale);preview.enabled=false;
                var baseObject=new GameObject("BlockedCells");baseObject.transform.SetParent(root.transform,false);
                var footprint=baseObject.AddComponent<SpriteRenderer>();
                footprint.sprite=PlaceholderArt.Floor(item.SizeX,item.SizeY,new Color(.3f,1,.45f,.8f),new Color(.3f,1,.45f,.8f));
                footprint.transform.localPosition=IsoProjection.GridToWorld(origin);
                footprint.sortingOrder=renderer.sortingOrder-1;
                Capture(root,item,facing);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Capture(GameObject root,PlacedFurniture item,Facing facing)
        {
            var cameraObject=new GameObject("MachineProofCamera");var camera=cameraObject.AddComponent<Camera>();
            var target=new RenderTexture(800,600,24);var pixels=new Texture2D(800,600,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;var children=root.GetComponentsInChildren<Transform>(true);
            var layers=children.Select(t=>t.gameObject.layer).ToArray();
            try
            {
                foreach(var child in children)child.gameObject.layer=31;
                camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=1.7f;
                var centre=IsoProjection.GridToWorld(item.Origin.X+item.SizeX*.5f,item.Origin.Y+item.SizeY*.5f);
                camera.transform.position=new Vector3(centre.x,centre.y+.4f,-10);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.55f,.75f,.63f);camera.targetTexture=target;
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,800,600),0,0);pixels.Apply();
                Directory.CreateDirectory("Logs");File.WriteAllBytes("Logs/pinball-footprint-"+facing+".png",pixels.EncodeToPNG());
            }
            finally
            {
                for(int i=0;i<children.Length;i++)children[i].gameObject.layer=layers[i];
                RenderTexture.active=previous;camera.targetTexture=null;
                UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
